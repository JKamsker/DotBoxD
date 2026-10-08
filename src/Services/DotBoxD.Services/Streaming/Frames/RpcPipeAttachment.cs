using System.IO.Pipelines;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Streaming.Frames;

internal sealed class RpcPipeAttachment : RpcStreamAttachment
{
    private const int ChunkSize = 64 * 1024;
    // Small-segment probes show the win; larger segments do not justify coalescing's
    // extra sequence-copy work. Keep this threshold below the 16 KiB control.
    private const int MaxSegmentToCoalesce = 4 * 1024;
    private Pipe? _pipe;
    private readonly bool _completeReader;

    public RpcPipeAttachment(RpcStreamHandle handle, Pipe pipe, bool completeReader)
        : base(handle)
    {
        _pipe = pipe;
        _completeReader = completeReader;
    }

    internal override async Task PumpCoreAsync(
        RpcStreamManager streams,
        ISerializer serializer,
        CancellationToken ct)
    {
        var pipe = _pipe;
        if (pipe is null)
        {
            return;
        }

        Exception? pumpFailure = null;
        try
        {
            while (true)
            {
                var result = await pipe.Reader.ReadAsync(ct).ConfigureAwait(false);
                var remaining = result.Buffer;
                try
                {
                    if (result.IsCanceled)
                    {
                        return;
                    }

                    while (!remaining.IsEmpty)
                    {
                        // Coalesce only bytes already returned by this read; never wait to fill a frame.
                        var first = remaining.First;
                        var length = Math.Min(first.Length, ChunkSize);
                        if (first.Length <= MaxSegmentToCoalesce && !remaining.IsSingleSegment)
                        {
                            length = (int)Math.Min(remaining.Length, ChunkSize);
                            await streams.SendStreamItemAsync(Handle.StreamId, remaining.Slice(0, length), ct).ConfigureAwait(false);
                        }
                        else
                        {
                            await streams.SendStreamItemAsync(Handle.StreamId, first.Slice(0, length), ct).ConfigureAwait(false);
                        }
                        remaining = remaining.Slice(length);
                    }
                }
                finally
                {
                    // Leave unsent segments available when a borrowed pipe's read or send fails.
                    pipe.Reader.AdvanceTo(remaining.Start);
                }

                if (result.IsCompleted)
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            pumpFailure = ex;
            throw;
        }
        finally
        {
            await DisposeSourceAfterPumpAsync(pumpFailure).ConfigureAwait(false);
        }
    }

    private protected override async ValueTask DisposeSourceCoreAsync()
    {
        if (!_completeReader)
        {
            return;
        }

        try
        {
            await _pipe!.Reader.CompleteAsync().ConfigureAwait(false);
        }
        finally
        {
            _pipe = null;
        }
    }

    private protected override bool OwnsSource => _completeReader;
}

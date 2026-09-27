using System.Buffers;
using System.IO.Pipelines;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Streaming.Frames;

internal sealed class RpcPipeAttachment : RpcStreamAttachment
{
    private const int ChunkSize = 64 * 1024;
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
                        var chunk = GetNextChunk(remaining);
                        var length = chunk.Length;
                        await streams.SendStreamItemAsync(Handle.StreamId, chunk, ct).ConfigureAwait(false);
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

    private static ReadOnlyMemory<byte> GetNextChunk(ReadOnlySequence<byte> remaining)
    {
        foreach (var segment in remaining)
        {
            if (!segment.IsEmpty)
            {
                return segment.Slice(0, Math.Min(segment.Length, ChunkSize));
            }
        }

        throw new InvalidOperationException("Nonempty pipe buffer contained no bytes.");
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

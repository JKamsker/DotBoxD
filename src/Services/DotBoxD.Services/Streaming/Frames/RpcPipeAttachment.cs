using System.IO.Pipelines;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Streaming.Frames;

internal sealed class RpcPipeAttachment : RpcStreamAttachment
{
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
                var buffer = result.Buffer;
                try
                {
                    if (result.IsCanceled)
                    {
                        return;
                    }

                    foreach (var segment in buffer)
                    {
                        if (!segment.IsEmpty)
                        {
                            await streams.SendStreamItemAsync(Handle.StreamId, segment, ct).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    // A canceled read has not handed any of its buffered bytes to the sender.
                    pipe.Reader.AdvanceTo(result.IsCanceled ? buffer.Start : buffer.End);
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

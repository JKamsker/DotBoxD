using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Streaming.Frames;

internal sealed class RpcStreamChunk : IDisposable
{
    private readonly RpcStreamReceiver _owner;
    private DotBoxD.Services.Buffers.Payload? _frame;

    public RpcStreamChunk(
        RpcStreamReceiver owner,
        DotBoxD.Services.Buffers.Payload frame,
        ReadOnlyMemory<byte> payload)
    {
        _owner = owner;
        _frame = frame;
        Payload = payload;
    }

    public ReadOnlyMemory<byte> Payload { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _frame, null) is { } frame)
        {
            frame.Dispose();
            _owner.ReleaseCredit();
        }
    }

    public void DisposeWithoutCredit() =>
        Interlocked.Exchange(ref _frame, null)?.Dispose();
}

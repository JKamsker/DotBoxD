using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Streaming.Frames;

internal sealed class RpcStreamChunk : IDisposable
{
    private RpcStreamReceiver? _owner;
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

    public void Dispose() => DisposeCore(releaseCredit: true);

    public void DisposeWithoutCredit() => DisposeCore(releaseCredit: false);

    private void DisposeCore(bool releaseCredit)
    {
        if (Interlocked.Exchange(ref _frame, null) is { } frame)
        {
            // The frame claimant owns cleanup and the sole right to return credit.
            var owner = _owner!;
            _owner = null;
            frame.Dispose();
            if (releaseCredit)
            {
                owner.ReleaseCredit();
            }
        }
    }
}

using ShaRPC.Core.Buffers;
using ShaRPC.Core.Streaming;

namespace ShaRPC.Core;

internal sealed class RpcDispatchResult : IDisposable
{
    private Payload? _frame;

    public RpcDispatchResult(Payload frame, RpcStreamAttachment? stream)
    {
        _frame = frame;
        Stream = stream;
    }

    public Payload Frame => _frame ?? throw new ObjectDisposedException(nameof(RpcDispatchResult));

    public RpcStreamAttachment? Stream { get; }

    public void Dispose() => Interlocked.Exchange(ref _frame, null)?.Dispose();
}

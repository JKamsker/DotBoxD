using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core;

internal readonly struct RpcPeerInboundRequest
{
    public RpcPeerInboundRequest(
        Payload frame,
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> body,
        CancellationTokenSource requestCts)
    {
        Frame = frame;
        Request = request;
        MessageId = messageId;
        Body = body;
        RequestCts = requestCts;
    }

    public Payload Frame { get; }

    public RpcRequest Request { get; }

    public int MessageId { get; }

    public ReadOnlyMemory<byte> Body { get; }

    public CancellationTokenSource RequestCts { get; }
}

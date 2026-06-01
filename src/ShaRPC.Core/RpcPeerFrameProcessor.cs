using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;

namespace ShaRPC.Core;

internal sealed class RpcPeerFrameProcessor
{
    private readonly RpcPeerInboundDispatcher _inbound;
    private readonly RpcPeerOutboundInvoker _outbound;
    private readonly Action<int, MessageType, string> _protocolError;

    public RpcPeerFrameProcessor(
        RpcPeerInboundDispatcher inbound,
        RpcPeerOutboundInvoker outbound,
        Action<int, MessageType, string> protocolError)
    {
        _inbound = inbound;
        _outbound = outbound;
        _protocolError = protocolError;
    }

    public async ValueTask<bool> ShouldDisposeAsync(Payload frame, CancellationToken ct)
    {
        if (!MessageFramer.TryReadFrameHeader(frame.Memory, out var messageId, out var messageType))
        {
            _protocolError(0, default, "Malformed frame header.");
            return true;
        }

        switch (messageType)
        {
            case MessageType.Response:
            case MessageType.Error:
                return !_outbound.TryCompleteResponse(messageId, frame);
            case MessageType.Request:
                return !await _inbound.AcceptRequestAsync(frame, messageId, ct).ConfigureAwait(false);
            case MessageType.Cancel:
                _inbound.Cancel(messageId);
                return true;
            default:
                _protocolError(messageId, messageType, "Unknown message type.");
                return true;
        }
    }
}

using DotBoxD.Services.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Peer;

internal sealed class RpcPeerFrameProcessor
{
    private readonly RpcPeerInboundDispatcher _inbound;
    private readonly RpcPeerOutboundInvoker _outbound;
    private readonly RpcStreamManager _streams;
    private readonly Action<int, MessageType, string, Exception?> _protocolError;
    private readonly Dictionary<MessageType, FrameHandler> _handlers;

    private delegate ValueTask<bool> FrameHandler(
        RpcFrame frame,
        int messageId,
        MessageType messageType,
        CancellationToken ct);

    public RpcPeerFrameProcessor(
        RpcPeerInboundDispatcher inbound,
        RpcPeerOutboundInvoker outbound,
        RpcStreamManager streams,
        Action<int, MessageType, string, Exception?> protocolError)
    {
        _inbound = inbound;
        _outbound = outbound;
        _streams = streams;
        _protocolError = protocolError;
        _handlers = new Dictionary<MessageType, FrameHandler>
        {
            [MessageType.Response] = HandleResponseAsync,
            [MessageType.Error] = HandleResponseAsync,
            [MessageType.Request] = HandleRequestAsync,
            [MessageType.Cancel] = HandleCancelAsync,
            [MessageType.StreamCancel] = HandleStreamCancelAsync,
            [MessageType.StreamItem] = HandleStreamItemAsync,
            [MessageType.StreamComplete] = HandleStreamCompleteAsync,
            [MessageType.StreamError] = HandleStreamErrorAsync,
            [MessageType.StreamCredit] = HandleStreamCreditAsync,
        };
    }

    public async ValueTask<bool> ShouldDisposeAsync(RpcFrame frame, CancellationToken ct)
    {
        if (!MessageFrameReader.TryReadFrameHeaderUnchecked(frame.Memory, out var messageId, out var messageType))
        {
            _protocolError(0, default, "Malformed frame header.", null);
            return true;
        }

        if (_handlers.TryGetValue(messageType, out var handler))
        {
            return await handler(frame, messageId, messageType, ct).ConfigureAwait(false);
        }

        _protocolError(messageId, messageType, "Unknown message type.", null);
        return true;
    }

    private ValueTask<bool> HandleResponseAsync(
        RpcFrame frame,
        int messageId,
        MessageType messageType,
        CancellationToken ct)
        => new(!_outbound.TryCompleteResponse(messageId, frame));

    private async ValueTask<bool> HandleRequestAsync(
        RpcFrame frame,
        int messageId,
        MessageType messageType,
        CancellationToken ct)
        => !await _inbound.AcceptRequestAsync(frame, messageId, ct).ConfigureAwait(false);

    private ValueTask<bool> HandleCancelAsync(
        RpcFrame frame,
        int messageId,
        MessageType messageType,
        CancellationToken ct)
    {
        if (messageId == 0 || frame.Length != MessageFramer.HeaderSize)
        {
            _protocolError(messageId, messageType, "Malformed cancel frame.", null);
            return new ValueTask<bool>(true);
        }

        _inbound.Cancel(messageId);
        return new ValueTask<bool>(true);
    }

    private ValueTask<bool> HandleStreamCancelAsync(
        RpcFrame frame,
        int messageId,
        MessageType messageType,
        CancellationToken ct)
    {
        if (!RpcStreamControlFrameReader.TryRead(frame.Memory, MessageType.StreamCancel, out var streamCancelId))
        {
            _protocolError(messageId, messageType, "Malformed stream cancel frame.", null);
            return new ValueTask<bool>(true);
        }

        _streams.CancelOutbound(streamCancelId);
        return new ValueTask<bool>(true);
    }

    private ValueTask<bool> HandleStreamItemAsync(
        RpcFrame frame,
        int messageId,
        MessageType messageType,
        CancellationToken ct)
    {
        if (messageId == 0)
        {
            _protocolError(messageId, messageType, "Malformed stream item frame.", null);
            return new ValueTask<bool>(true);
        }

        var itemFrame = frame.DetachPayload();
        if (_streams.TryAcceptItem(messageId, itemFrame))
        {
            return new ValueTask<bool>(false);
        }

        itemFrame.Dispose();
        _protocolError(messageId, messageType, "Unknown stream id.", null);
        return new ValueTask<bool>(true);
    }

    private ValueTask<bool> HandleStreamCompleteAsync(
        RpcFrame frame,
        int messageId,
        MessageType messageType,
        CancellationToken ct)
    {
        if (!RpcStreamCompleteFrameReader.TryRead(frame.Memory, out var streamId))
        {
            _protocolError(messageId, messageType, "Malformed stream complete frame.", null);
            return new ValueTask<bool>(true);
        }

        if (!_streams.TryCompleteInbound(streamId))
        {
            _protocolError(messageId, messageType, "Unknown stream id.", null);
        }

        return new ValueTask<bool>(true);
    }

    private ValueTask<bool> HandleStreamErrorAsync(
        RpcFrame frame,
        int messageId,
        MessageType messageType,
        CancellationToken ct)
    {
        if (!_streams.TryCompleteInboundError(frame.Memory, out var malformed))
        {
            var message = malformed ? "Malformed stream error frame." : "Unknown stream id.";
            _protocolError(messageId, messageType, message, null);
        }

        return new ValueTask<bool>(true);
    }

    private ValueTask<bool> HandleStreamCreditAsync(
        RpcFrame frame,
        int messageId,
        MessageType messageType,
        CancellationToken ct)
    {
        if (!_streams.TryAddCredit(frame.Memory))
        {
            _protocolError(messageId, messageType, "Malformed stream credit frame.", null);
        }

        return new ValueTask<bool>(true);
    }

    public ValueTask<bool> ShouldDisposeAsync(Payload frame, CancellationToken ct) =>
        ShouldDisposeAsync(new RpcFrame(frame), ct);
}

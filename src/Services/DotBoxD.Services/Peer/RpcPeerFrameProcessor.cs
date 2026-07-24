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
    }

    public ValueTask<bool> ShouldDisposeAsync(RpcFrame frame, CancellationToken ct)
    {
        if (!MessageFrameReader.TryReadFrameHeaderUnchecked(frame.Memory, out var messageId, out var messageType))
        {
            _protocolError(0, default, "Malformed frame header.", null);
            return new ValueTask<bool>(true);
        }

        return messageType switch
        {
            MessageType.Response or MessageType.Error => HandleResponse(frame, messageId),
            MessageType.Request => HandleRequestAsync(frame, messageId, ct),
            MessageType.Cancel => HandleCancel(frame, messageId, messageType),
            _ => HandleStreamingOrUnknown(frame, messageId, messageType),
        };
    }

    private ValueTask<bool> HandleStreamingOrUnknown(
        RpcFrame frame,
        int messageId,
        MessageType messageType) =>
        messageType switch
        {
            MessageType.StreamCancel => HandleStreamCancel(frame, messageId, messageType),
            MessageType.StreamItem => HandleStreamItem(frame, messageId, messageType),
            MessageType.StreamComplete => HandleStreamComplete(frame, messageId, messageType),
            MessageType.StreamError => HandleStreamError(frame, messageId, messageType),
            MessageType.StreamCredit => HandleStreamCredit(frame, messageId, messageType),
            _ => HandleUnknown(messageId, messageType),
        };

    private ValueTask<bool> HandleResponse(RpcFrame frame, int messageId)
        => new(!_outbound.TryCompleteResponse(messageId, frame));

    private async ValueTask<bool> HandleRequestAsync(
        RpcFrame frame,
        int messageId,
        CancellationToken ct)
        => !await _inbound.AcceptRequestAsync(frame, messageId, ct).ConfigureAwait(false);

    private ValueTask<bool> HandleCancel(
        RpcFrame frame,
        int messageId,
        MessageType messageType)
    {
        if (messageId == 0 || frame.Length != MessageFramer.HeaderSize)
        {
            _protocolError(messageId, messageType, "Malformed cancel frame.", null);
            return new ValueTask<bool>(true);
        }

        _inbound.Cancel(messageId);
        return new ValueTask<bool>(true);
    }

    private ValueTask<bool> HandleStreamCancel(
        RpcFrame frame,
        int messageId,
        MessageType messageType)
    {
        if (!RpcStreamControlFrameReader.TryRead(frame.Memory, MessageType.StreamCancel, out var streamCancelId))
        {
            _protocolError(messageId, messageType, "Malformed stream cancel frame.", null);
            return new ValueTask<bool>(true);
        }

        _streams.CancelOutbound(streamCancelId);
        return new ValueTask<bool>(true);
    }

    private ValueTask<bool> HandleStreamItem(
        RpcFrame frame,
        int messageId,
        MessageType messageType)
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

    private ValueTask<bool> HandleStreamComplete(
        RpcFrame frame,
        int messageId,
        MessageType messageType)
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

    private ValueTask<bool> HandleStreamError(
        RpcFrame frame,
        int messageId,
        MessageType messageType)
    {
        if (!_streams.TryCompleteInboundError(frame.Memory, out var malformed))
        {
            var message = malformed ? "Malformed stream error frame." : "Unknown stream id.";
            _protocolError(messageId, messageType, message, null);
        }

        return new ValueTask<bool>(true);
    }

    private ValueTask<bool> HandleStreamCredit(
        RpcFrame frame,
        int messageId,
        MessageType messageType)
    {
        if (!_streams.TryAddCredit(frame.Memory))
        {
            _protocolError(messageId, messageType, "Malformed stream credit frame.", null);
        }

        return new ValueTask<bool>(true);
    }

    private ValueTask<bool> HandleUnknown(int messageId, MessageType messageType)
    {
        _protocolError(messageId, messageType, "Unknown message type.", null);
        return new ValueTask<bool>(true);
    }

    public ValueTask<bool> ShouldDisposeAsync(Payload frame, CancellationToken ct) =>
        ShouldDisposeAsync(new RpcFrame(frame), ct);
}

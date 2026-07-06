using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Client;

internal sealed partial class RpcPeerOutboundInvoker
{
    public bool TryCompleteResponse(int messageId, RpcFrame frame)
    {
        if (!MessageFramer.TryReadFrame(frame.Memory, out _, out var messageType, out var envelope, out var payload))
        {
            FailPending(messageId, "Malformed response frame.");
            return false;
        }

        if (!TryReadResponseEnvelope(messageId, envelope, out var response))
        {
            return false;
        }

        if (!ValidateResponseFrame(messageId, messageType, payload.Length, response))
        {
            return false;
        }

        if (!_pending.TryTake(messageId, out var completion))
        {
            return false;
        }

        RpcStreamReceiver? stream = null;
        try
        {
            if (response.Stream is { } handle &&
                completion.RegistersStreamingResponse)
            {
                stream = _streams.RegisterInboundResponse(handle, CancellationToken.None);
            }

            return completion.TrySetResponse(response, payload, frame, stream, _serializer);
        }
        catch (Exception ex)
        {
            stream?.Cancel();
            completion.SetError(ex);
            return false;
        }
    }

    private bool TryReadResponseEnvelope(int messageId, ReadOnlyMemory<byte> envelope, out RpcResponse response)
    {
        try
        {
            response = _serializer.Deserialize<RpcResponse>(envelope);
            return true;
        }
        catch
        {
            response = default;
            FailPending(messageId, "Malformed response envelope.");
            return false;
        }
    }

    private bool ValidateResponseFrame(
        int messageId,
        MessageType messageType,
        int payloadLength,
        RpcResponse response)
    {
        if (response.MessageId != messageId)
        {
            FailPending(messageId, "Response envelope message id does not match frame header.");
            return false;
        }

        if (messageType == MessageType.Error && response.IsSuccess)
        {
            FailPending(messageId, "Malformed error response frame.");
            return false;
        }

        if (messageType == MessageType.Error && payloadLength != 0)
        {
            FailPending(messageId, "Error response payload is not allowed.");
            return false;
        }

        return true;
    }

    private void FailPending(int messageId, string message)
        => _pending.TryFail(messageId, new ServiceProtocolException(message));

    public bool TryCompleteResponse(int messageId, Payload frame) =>
        TryCompleteResponse(messageId, new RpcFrame(frame));

    private TResponse DeserializeNonStreamingResponse<TResponse>(ReceivedResponse received)
    {
        EnsureNonStreamingResponse(received);
        return _serializer.Deserialize<TResponse>(received.Payload);
    }

    private static void EnsureNonStreamingResponse(ReceivedResponse received)
    {
        if (received.Response.Stream is not null)
        {
            throw new ServiceProtocolException(
                "Response opened a stream for a non-streaming invocation.");
        }
    }

    private static void EnsureNoResponsePayload(ReceivedResponse received)
    {
        EnsureNonStreamingResponse(received);
        if (received.Payload.Length != 0)
        {
            throw new ServiceProtocolException(
                "Response payload is not allowed for a no-response invocation.");
        }
    }
}

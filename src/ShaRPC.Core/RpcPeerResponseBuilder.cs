using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;

namespace ShaRPC.Core;

internal sealed class RpcPeerResponseBuilder
{
    private readonly ISerializer _serializer;
    private readonly InstanceRegistry _registry;
    private readonly ConcurrentDictionary<string, IServiceDispatcher> _dispatchers;
    private readonly bool _rejectInboundCalls;

    public RpcPeerResponseBuilder(
        ISerializer serializer,
        InstanceRegistry registry,
        ConcurrentDictionary<string, IServiceDispatcher> dispatchers,
        bool rejectInboundCalls)
    {
        _serializer = serializer;
        _registry = registry;
        _dispatchers = dispatchers;
        _rejectInboundCalls = rejectInboundCalls;
    }

    public async ValueTask<Payload> BuildAsync(
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct)
    {
        if (_rejectInboundCalls)
        {
            return BuildErrorFrame(messageId, "This peer does not accept inbound calls.", "ShaRpcInboundRejected");
        }

        if (!_dispatchers.TryGetValue(request.ServiceName, out var dispatcher))
        {
            return BuildErrorFrame(messageId, $"Service '{request.ServiceName}' not found.", nameof(ShaRpcNotFoundException));
        }

        using var writer = new PooledBufferWriter(MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize);
        MessageFramer.WriteFramePrefix(writer, messageId, MessageType.Response);
        var envelopeStart = writer.WrittenCount;
        _serializer.Serialize(writer, new RpcResponse { MessageId = messageId, IsSuccess = true });
        var envelopeLength = writer.WrittenCount - envelopeStart;

        try
        {
            await (request.InstanceId is null
                ? dispatcher.DispatchAsync(request.MethodName, payload, _serializer, _registry, writer, ct)
                : dispatcher.DispatchOnInstanceAsync(request.InstanceId, request.MethodName, payload, _serializer, _registry, writer, ct)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildErrorFrame(messageId, ex.Message, ex.GetType().Name);
        }

        return MessageFramer.FinishFrame(writer, envelopeLength);
    }

    public Payload BuildProtocolErrorFrame(int messageId, string errorMessage) =>
        BuildErrorFrame(messageId, errorMessage, nameof(ShaRpcProtocolException));

    private Payload BuildErrorFrame(int messageId, string errorMessage, string errorType) =>
        MessageFramer.FrameMessage(
            _serializer,
            messageId,
            MessageType.Error,
            new RpcResponse
            {
                MessageId = messageId,
                IsSuccess = false,
                ErrorMessage = errorMessage,
                ErrorType = errorType,
            },
            ReadOnlySpan<byte>.Empty);
}

using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Core.Server;

internal sealed class ShaRpcServerResponseBuilder
{
    private readonly ISerializer _serializer;
    private readonly ConcurrentDictionary<string, IServiceDispatcher> _dispatchers;

    public ShaRpcServerResponseBuilder(
        ISerializer serializer,
        ConcurrentDictionary<string, IServiceDispatcher> dispatchers)
    {
        _serializer = serializer;
        _dispatchers = dispatchers;
    }

    public async ValueTask<Payload> BuildAsync(
        RpcRequest request,
        int messageId,
        ReadOnlyMemory<byte> payload,
        IInstanceRegistry registry,
        CancellationToken ct)
    {
        if (!_dispatchers.TryGetValue(request.ServiceName, out var dispatcher))
        {
            return BuildErrorFrame(messageId, RpcErrors.ServiceNotFound(request.ServiceName));
        }

        using var writer = new PooledBufferWriter(MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize);
        MessageFramer.WriteFramePrefix(writer, messageId, MessageType.Response);
        var envelopeStart = writer.WrittenCount;
        _serializer.Serialize(writer, new RpcResponse { MessageId = messageId, IsSuccess = true });
        var envelopeLength = writer.WrittenCount - envelopeStart;

        try
        {
            await (request.InstanceId is null
                ? dispatcher.DispatchAsync(request.MethodName, payload, _serializer, registry, writer, ct)
                : dispatcher.DispatchOnInstanceAsync(request.InstanceId, request.MethodName, payload, _serializer, registry, writer, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return BuildErrorFrame(messageId, RpcErrors.FromException(ex));
        }

        return MessageFramer.FinishFrame(writer, envelopeLength);
    }

    private Payload BuildErrorFrame(int messageId, RpcError error) =>
        MessageFramer.FrameMessage(
            _serializer,
            messageId,
            MessageType.Error,
            new RpcResponse
            {
                MessageId = messageId,
                IsSuccess = false,
                ErrorMessage = error.Message,
                ErrorType = error.Type,
            },
            ReadOnlySpan<byte>.Empty);
}

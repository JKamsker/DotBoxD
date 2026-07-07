using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;

namespace DotBoxD.Services.Tests.Coverage.Peer;

internal static class PeerOutboundTestSupport
{
    internal const string Service = "Svc";
    internal const string Method = "Op";
    internal static readonly TimeSpan PeerOutboundTimeout = TimeSpan.FromSeconds(10);

    internal static MessagePackRpcSerializer NewSerializer() => new();

    internal static RpcPeerOptions Options(TimeSpan? requestTimeout = null) =>
        new() { RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5) };

    internal static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not satisfied within the timeout.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    internal static Payload ResponseFrame<TResult>(
        ISerializer serializer,
        int messageId,
        TResult result,
        bool isSuccess = true,
        MessageType type = MessageType.Response)
    {
        var response = new RpcResponse
        {
            MessageId = messageId,
            IsSuccess = isSuccess,
        };

        var payloadWriter = new ArrayBufferWriter<byte>();
        serializer.Serialize(payloadWriter, result);
        return MessageFramer.FrameMessage(serializer, messageId, type, response, payloadWriter.WrittenSpan);
    }

    internal static Payload ErrorFrame(
        ISerializer serializer,
        int messageId,
        string errorMessage,
        string errorType)
    {
        var response = new RpcResponse
        {
            MessageId = messageId,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType,
        };

        return MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Error,
            response,
            ReadOnlySpan<byte>.Empty);
    }
}

using System.Buffers;
using System.Collections.Concurrent;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Server.DispatchCancellation;

public sealed class RpcDispatchResponseBuilderCancellationRegressionTests
{
    [Fact]
    public async Task BuildAsync_observes_custom_dispatcher_canceled_token_before_returning_success()
    {
        using var source = new CancellationTokenSource();
        var serializer = new MessagePackRpcSerializer();
        var dispatcher = new CancelingSuccessDispatcher(source);
        var dispatchers = new ConcurrentDictionary<string, IServiceDispatcher>();
        Assert.True(dispatchers.TryAdd(dispatcher.ServiceName, dispatcher));
        var builder = new RpcDispatchResponseBuilder(serializer, dispatchers);
        var request = new RpcRequest
        {
            MessageId = 42,
            ServiceName = dispatcher.ServiceName,
            MethodName = "Cancel",
        };

        var returned = false;
        MessageType? returnedMessageType = null;
        var returnedPayloadBytes = 0;

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var result = await builder.BuildAsync(
                request,
                messageId: 42,
                ReadOnlyMemory<byte>.Empty,
                new InstanceRegistry(),
                RpcStreamingContext.Disabled,
                source.Token);

            returned = true;
            if (MessageFramer.TryReadFrame(
                result.FrameMemory,
                out _,
                out var messageType,
                out _,
                out var responsePayload))
            {
                returnedMessageType = messageType;
                returnedPayloadBytes = responsePayload.Length;
            }
        });

        Assert.True(
            exception is OperationCanceledException,
            "Expected OperationCanceledException after custom dispatcher canceled the request token; " +
            $"actual {exception?.GetType().Name ?? "no exception"}, " +
            $"returned {returned}, " +
            $"token canceled {source.IsCancellationRequested}, " +
            $"message type {returnedMessageType?.ToString() ?? "unreadable"}, " +
            $"payload bytes {returnedPayloadBytes}.");
        Assert.True(source.IsCancellationRequested);
        Assert.Equal(1, dispatcher.CallCount);
    }

    private sealed class CancelingSuccessDispatcher(CancellationTokenSource source) :
        IServiceDispatcher,
        INonStreamingServiceDispatcher
    {
        public string ServiceName => "CancelingSuccess";

        public int CallCount { get; private set; }

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            CallCount++;
            source.Cancel();
            serializer.Serialize(output, 123);
            return Task.CompletedTask;
        }
    }
}

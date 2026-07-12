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
    public async Task BuildAsync_observes_pre_canceled_token_before_custom_dispatcher_work()
    {
        var serializer = new MessagePackRpcSerializer();
        var dispatcher = new CountingDispatcher();
        var dispatchers = new ConcurrentDictionary<string, IServiceDispatcher>();
        Assert.True(dispatchers.TryAdd(dispatcher.ServiceName, dispatcher));
        var builder = new RpcDispatchResponseBuilder(serializer, dispatchers);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var returned = false;
        MessageType? returnedMessageType = null;
        var returnedPayloadBytes = 0;

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var result = await builder.BuildAsync(
                new RpcRequest
                {
                    MessageId = 42,
                    ServiceName = dispatcher.ServiceName,
                    MethodName = "Count",
                },
                messageId: 42,
                ReadOnlyMemory<byte>.Empty,
                new InstanceRegistry(),
                RpcStreamingContext.Disabled,
                canceled.Token);

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
            "Expected OperationCanceledException before custom dispatcher work; actual " +
            $"{exception?.GetType().Name ?? "no exception"}, " +
            $"dispatcher calls {dispatcher.CallCount}, " +
            $"dispatcher output bytes {dispatcher.OutputBytes}, " +
            $"returned {returned}, " +
            $"message type {returnedMessageType?.ToString() ?? "none"}, " +
            $"payload bytes {returnedPayloadBytes}.");
        Assert.Equal(0, dispatcher.CallCount);
        Assert.Equal(0, dispatcher.OutputBytes);
        Assert.False(returned);
        Assert.Null(returnedMessageType);
        Assert.Equal(0, returnedPayloadBytes);
    }

    private sealed class CountingDispatcher : IServiceDispatcher, INonStreamingServiceDispatcher
    {
        public string ServiceName => "Counting";

        public int CallCount { get; private set; }

        public int OutputBytes { get; private set; }

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            CallCount++;
            var span = output.GetSpan(1);
            span[0] = 0x2A;
            output.Advance(1);
            OutputBytes++;
            return Task.CompletedTask;
        }
    }
}

using System.Buffers;
using System.Collections.Concurrent;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Remote;
using Xunit;

namespace DotBoxD.Services.Tests.Server.DispatchCancellation;

public sealed class RpcDispatchResponseBuilderDispatcherFaultCancellationRegressionTests
{
    [Fact]
    public async Task BuildAsync_prefers_dispatcher_canceled_request_token_over_dispatcher_fault()
    {
        using var cancellation = new CancellationTokenSource();
        var serializer = new MessagePackRpcSerializer();
        var dispatcher = new CancelThenFaultDispatcher(cancellation);
        var builder = CreateBuilder(serializer, dispatcher);

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var result = await builder.BuildAsync(
                CreateRequest(dispatcher.ServiceName),
                messageId: 42,
                ReadOnlyMemory<byte>.Empty,
                new InstanceRegistry(),
                RpcStreamingContext.Disabled,
                cancellation.Token);
        });

        var canceled = Assert.IsType<OperationCanceledException>(exception);
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, dispatcher.CallCount);
    }

    [Fact]
    public async Task BuildAsync_returns_dispatcher_fault_as_error_while_request_token_is_live()
    {
        var serializer = new MessagePackRpcSerializer();
        var dispatcher = new CancelThenFaultDispatcher(cancellation: null);
        var builder = CreateBuilder(serializer, dispatcher);

        using var result = await builder.BuildAsync(
            CreateRequest(dispatcher.ServiceName),
            messageId: 42,
            ReadOnlyMemory<byte>.Empty,
            new InstanceRegistry(),
            RpcStreamingContext.Disabled,
            CancellationToken.None);

        Assert.Equal(1, dispatcher.CallCount);
        Assert.True(MessageFramer.TryReadFrame(
            result.FrameMemory,
            out _,
            out var messageType,
            out var envelope,
            out _));
        Assert.Equal(MessageType.Error, messageType);

        var response = serializer.Deserialize<RpcResponse>(envelope);
        Assert.False(response.IsSuccess);
        Assert.Equal(RpcErrorTypes.InternalError, response.ErrorType);
    }

    private static RpcDispatchResponseBuilder CreateBuilder(
        ISerializer serializer,
        IServiceDispatcher dispatcher)
    {
        var dispatchers = new ConcurrentDictionary<string, IServiceDispatcher>();
        Assert.True(dispatchers.TryAdd(dispatcher.ServiceName, dispatcher));
        return new RpcDispatchResponseBuilder(serializer, dispatchers);
    }

    private static RpcRequest CreateRequest(string serviceName) =>
        new()
        {
            MessageId = 42,
            ServiceName = serviceName,
            MethodName = "Fault",
        };

    private sealed class CancelThenFaultDispatcher(CancellationTokenSource? cancellation) :
        IServiceDispatcher,
        INonStreamingServiceDispatcher
    {
        public string ServiceName => "CancelThenFault";

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
            cancellation?.Cancel();
            return Task.FromException(new InvalidOperationException("dispatcher fault"));
        }
    }
}

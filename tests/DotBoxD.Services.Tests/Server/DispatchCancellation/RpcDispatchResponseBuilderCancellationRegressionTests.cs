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
        var builder = CreateBuilder(serializer, dispatcher);
        var request = new RpcRequest { MessageId = 42, ServiceName = dispatcher.ServiceName, MethodName = "Cancel" };

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var result = await builder.BuildAsync(
                request,
                messageId: 42,
                ReadOnlyMemory<byte>.Empty,
                new InstanceRegistry(),
                RpcStreamingContext.Disabled,
                source.Token);
        });

        Assert.IsType<OperationCanceledException>(exception);
        Assert.True(source.IsCancellationRequested);
        Assert.Equal(1, dispatcher.CallCount);
    }

    [Fact]
    public async Task BuildAsync_observes_pre_canceled_token_before_custom_dispatcher_work()
    {
        var serializer = new MessagePackRpcSerializer();
        var dispatcher = new CountingDispatcher();
        var builder = CreateBuilder(serializer, dispatcher);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var result = await builder.BuildAsync(
                new RpcRequest { MessageId = 42, ServiceName = dispatcher.ServiceName, MethodName = "Count" },
                messageId: 42,
                ReadOnlyMemory<byte>.Empty,
                new InstanceRegistry(),
                RpcStreamingContext.Disabled,
                canceled.Token);
        });

        Assert.IsType<OperationCanceledException>(exception);
        Assert.Equal(0, dispatcher.CallCount);
        Assert.Equal(0, dispatcher.OutputBytes);
    }

    [Fact]
    public async Task BuildAsync_observes_serializer_canceled_token_before_custom_dispatcher_work()
    {
        using var source = new CancellationTokenSource();
        var serializer = new ResponseCancelingSerializer(new MessagePackRpcSerializer(), source);
        var dispatcher = new CountingDispatcher();
        var builder = CreateBuilder(serializer, dispatcher);
        var request = new RpcRequest { MessageId = 42, ServiceName = dispatcher.ServiceName, MethodName = "Count" };

        var exception = await Record.ExceptionAsync(async () =>
        {
            using var result = await builder.BuildAsync(
                request,
                messageId: 42,
                ReadOnlyMemory<byte>.Empty,
                new InstanceRegistry(),
                RpcStreamingContext.Disabled,
                source.Token);
        });

        Assert.IsType<OperationCanceledException>(exception);
        Assert.True(source.IsCancellationRequested);
        Assert.Equal(1, serializer.SerializeResponseCalls);
        Assert.Equal(0, dispatcher.CallCount);
        Assert.Equal(0, dispatcher.OutputBytes);
    }

    private static RpcDispatchResponseBuilder CreateBuilder(
        ISerializer serializer,
        IServiceDispatcher dispatcher)
    {
        var dispatchers = new ConcurrentDictionary<string, IServiceDispatcher>();
        Assert.True(dispatchers.TryAdd(dispatcher.ServiceName, dispatcher));
        return new RpcDispatchResponseBuilder(serializer, dispatchers);
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

    private sealed class ResponseCancelingSerializer(ISerializer inner, CancellationTokenSource source) : ISerializer
    {
        public int SerializeResponseCalls { get; private set; }

        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            if (value is RpcResponse { IsSuccess: true })
            {
                SerializeResponseCalls++;
                source.Cancel();
            }

            inner.Serialize(writer, value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => inner.Deserialize<T>(data);

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => inner.Deserialize(data, type);
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

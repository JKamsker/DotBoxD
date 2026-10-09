using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.GeneratedFixtures;
using Xunit;

namespace DotBoxD.Services.Tests.Server.DispatchCancellation;

public sealed class GeneratedDispatcherReceiverFaultCancellationRegressionTests
{
    [Fact]
    public async Task Generated_dispatcher_prefers_receiver_canceled_token_over_receiver_task_fault()
    {
        using var source = new CancellationTokenSource();
        var service = new CancelThenFaultService(source);
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<IReceiverFaultCancellationService>(service);
        var method = FindMethod();
        var innerSerializer = new MessagePackRpcSerializer();
        using var payload = innerSerializer.SerializeToPayload(123);
        var serializer = new CountingSerializer(innerSerializer);
        var output = new ArrayBufferWriter<byte>();

        var exception = await Record.ExceptionAsync(() =>
            dispatcher.DispatchAsync(
                method.WireName,
                payload.Memory,
                serializer,
                new InstanceRegistry(),
                output,
                source.Token));

        var canceled = Assert.IsType<OperationCanceledException>(exception);
        Assert.Equal(source.Token, canceled.CancellationToken);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(1, serializer.DeserializeCalls);
        Assert.Equal(0, serializer.SerializeCalls);
        Assert.Equal(0, output.WrittenCount);
    }

    [Fact]
    public async Task Generated_dispatcher_preserves_receiver_task_fault_while_token_is_live()
    {
        using var source = new CancellationTokenSource();
        var service = new CancelThenFaultService(cancellation: null);
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<IReceiverFaultCancellationService>(service);
        var method = FindMethod();
        var innerSerializer = new MessagePackRpcSerializer();
        using var payload = innerSerializer.SerializeToPayload(123);
        var serializer = new CountingSerializer(innerSerializer);
        var output = new ArrayBufferWriter<byte>();

        var exception = await Record.ExceptionAsync(() =>
            dispatcher.DispatchAsync(
                method.WireName,
                payload.Memory,
                serializer,
                new InstanceRegistry(),
                output,
                source.Token));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.False(source.IsCancellationRequested);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(1, serializer.DeserializeCalls);
        Assert.Equal(0, serializer.SerializeCalls);
        Assert.Equal(0, output.WrittenCount);
    }

    private static GeneratedMethod FindMethod() =>
        Assert.Single(GeneratedServiceRegistry.GetService<IReceiverFaultCancellationService>().Methods);

    private sealed class CancelThenFaultService(CancellationTokenSource? cancellation) : IReceiverFaultCancellationService
    {
        public int CallCount { get; private set; }

        public Task<int> FaultAsync(int value, CancellationToken ct = default)
        {
            CallCount++;
            cancellation?.Cancel();
            return Task.FromException<int>(new InvalidOperationException("receiver fault"));
        }
    }

    private sealed class CountingSerializer(ISerializer inner) : ISerializer
    {
        public int DeserializeCalls { get; private set; }

        public int SerializeCalls { get; private set; }

        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            SerializeCalls++;
            inner.Serialize(writer, value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data)
        {
            DeserializeCalls++;
            return inner.Deserialize<T>(data);
        }

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
        {
            DeserializeCalls++;
            return inner.Deserialize(data, type);
        }
    }
}

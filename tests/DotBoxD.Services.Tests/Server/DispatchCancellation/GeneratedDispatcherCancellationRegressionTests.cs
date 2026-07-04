using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.GeneratedFixtures;
using Xunit;

namespace DotBoxD.Services.Tests.Server;

public sealed class GeneratedDispatcherCancellationRegressionTests
{
    [Fact]
    public async Task Generated_dispatcher_observes_pre_canceled_token_before_payload_and_receiver_work()
    {
        var service = new RecordingDispatchCancellationService();
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<IDispatchCancellationService>(service);
        var method = Assert.Single(
            GeneratedServiceRegistry.GetService<IDispatchCancellationService>().Methods,
            static candidate => candidate.Name == nameof(IDispatchCancellationService.Record));
        var innerSerializer = new MessagePackRpcSerializer();
        using var payload = innerSerializer.SerializeToPayload(123);
        var serializer = new CountingSerializer(innerSerializer);
        var registry = new InstanceRegistry();
        var output = new ArrayBufferWriter<byte>();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var exception = await Record.ExceptionAsync(() =>
            dispatcher.DispatchAsync(
                method.WireName,
                payload.Memory,
                serializer,
                registry,
                output,
                canceled.Token));

        Assert.True(
            exception is OperationCanceledException,
            "Expected OperationCanceledException before dispatch work; actual " +
            $"{exception?.GetType().Name ?? "no exception"}, " +
            $"deserialize calls {serializer.DeserializeCalls}, " +
            $"receiver calls {service.CallCount}, " +
            $"serialize calls {serializer.SerializeCalls}, " +
            $"bytes written {output.WrittenCount}.");
        Assert.Equal(0, serializer.DeserializeCalls);
        Assert.Equal(0, service.CallCount);
        Assert.Equal(0, serializer.SerializeCalls);
        Assert.Equal(0, output.WrittenCount);
    }

    private sealed class RecordingDispatchCancellationService : IDispatchCancellationService
    {
        public int CallCount { get; private set; }

        public int Record(int value)
        {
            CallCount++;
            return value + 1;
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

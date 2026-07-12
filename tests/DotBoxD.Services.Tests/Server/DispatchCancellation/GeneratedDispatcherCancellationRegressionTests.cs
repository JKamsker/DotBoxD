using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.GeneratedFixtures;
using Xunit;

namespace DotBoxD.Services.Tests.Server.DispatchCancellation;

public sealed class GeneratedDispatcherCancellationRegressionTests
{
    [Fact]
    public async Task Generated_dispatcher_observes_pre_canceled_token_before_payload_and_receiver_work()
    {
        var service = new RecordingDispatchCancellationService();
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<IDispatchCancellationService>(service);
        var method = FindMethod(nameof(IDispatchCancellationService.Record));
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

    [Fact]
    public async Task Generated_instance_dispatcher_observes_pre_canceled_token_before_registry_lookup()
    {
        var service = new RecordingDispatchCancellationService();
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<IDispatchCancellationService>(service);
        var method = FindMethod(nameof(IDispatchCancellationService.Record));
        var innerSerializer = new MessagePackRpcSerializer();
        using var payload = innerSerializer.SerializeToPayload(123);
        var serializer = new CountingSerializer(innerSerializer);
        var registry = new CountingMissingInstanceRegistry();
        var output = new ArrayBufferWriter<byte>();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var exception = await Record.ExceptionAsync(() =>
            dispatcher.DispatchOnInstanceAsync(
                "missing-instance",
                method.WireName,
                payload.Memory,
                serializer,
                registry,
                output,
                canceled.Token));

        Assert.True(
            exception is OperationCanceledException,
            "Expected OperationCanceledException before instance registry lookup; actual " +
            $"{exception?.GetType().Name ?? "no exception"}, " +
            $"registry lookups {registry.TryGetCalls}, " +
            $"deserialize calls {serializer.DeserializeCalls}, " +
            $"receiver calls {service.CallCount}, " +
            $"serialize calls {serializer.SerializeCalls}, " +
            $"bytes written {output.WrittenCount}.");
        Assert.Equal(0, registry.TryGetCalls);
        Assert.Equal(0, serializer.DeserializeCalls);
        Assert.Equal(0, service.CallCount);
        Assert.Equal(0, serializer.SerializeCalls);
        Assert.Equal(0, output.WrittenCount);
    }

    [Fact]
    public async Task Generated_dispatcher_observes_receiver_canceled_token_before_serializing_scalar_result()
    {
        using var source = new CancellationTokenSource();
        var service = new ReceiverCancelingDispatchCancellationService(source);
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<IDispatchCancellationService>(service);
        var method = FindMethod(nameof(IDispatchCancellationService.RecordAfterCancelAsync));
        var innerSerializer = new MessagePackRpcSerializer();
        using var payload = innerSerializer.SerializeToPayload(123);
        var serializer = new CountingSerializer(innerSerializer);
        var registry = new InstanceRegistry();
        var output = new ArrayBufferWriter<byte>();

        var exception = await Record.ExceptionAsync(() =>
            dispatcher.DispatchAsync(
                method.WireName,
                payload.Memory,
                serializer,
                registry,
                output,
                source.Token));

        Assert.True(
            exception is OperationCanceledException,
            "Expected OperationCanceledException after receiver-canceled dispatch token; actual " +
            $"{exception?.GetType().Name ?? "no exception"}, " +
            $"deserialize calls {serializer.DeserializeCalls}, " +
            $"receiver calls {service.CallCount}, " +
            $"serialize calls {serializer.SerializeCalls}, " +
            $"bytes written {output.WrittenCount}.");
        Assert.Equal(1, serializer.DeserializeCalls);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(0, serializer.SerializeCalls);
        Assert.Equal(0, output.WrittenCount);
    }

    [Theory]
    [InlineData(nameof(IDispatchCancellationService.CancelVoid))]
    [InlineData(nameof(IDispatchCancellationService.CancelTaskAsync))]
    [InlineData(nameof(IDispatchCancellationService.CancelValueTaskAsync))]
    public async Task Generated_dispatcher_observes_receiver_canceled_token_before_completing_no_result_dispatch(
        string methodName)
    {
        using var source = new CancellationTokenSource();
        var service = new ReceiverCancelingDispatchCancellationService(source);
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<IDispatchCancellationService>(service);
        var method = FindMethod(methodName);
        var innerSerializer = new MessagePackRpcSerializer();
        var serializer = new CountingSerializer(innerSerializer);

        var registry = new InstanceRegistry();
        var output = new ArrayBufferWriter<byte>();

        var exception = await Record.ExceptionAsync(() =>
            dispatcher.DispatchAsync(
                method.WireName,
                ReadOnlyMemory<byte>.Empty,
                serializer,
                registry,
                output,
                source.Token));

        Assert.True(
            exception is OperationCanceledException,
            "Expected OperationCanceledException after receiver-canceled no-result dispatch token; actual " +
            $"{exception?.GetType().Name ?? "no exception"}, " +
            $"deserialize calls {serializer.DeserializeCalls}, " +
            $"receiver calls {service.CallCount}, " +
            $"serialize calls {serializer.SerializeCalls}, " +
            $"bytes written {output.WrittenCount}.");
        Assert.Equal(0, serializer.DeserializeCalls);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(0, serializer.SerializeCalls);
        Assert.Equal(0, output.WrittenCount);
    }

    [Fact]
    public async Task Generated_dispatcher_observes_deserializer_canceled_token_before_receiver_work()
    {
        using var source = new CancellationTokenSource();
        var service = new RecordingDispatchCancellationService();
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<IDispatchCancellationService>(service);
        var method = FindMethod(nameof(IDispatchCancellationService.Record));
        var innerSerializer = new MessagePackRpcSerializer();
        using var payload = innerSerializer.SerializeToPayload(123);
        var serializer = new CountingSerializer(innerSerializer, source.Cancel);
        var registry = new InstanceRegistry();
        var output = new ArrayBufferWriter<byte>();

        var exception = await Record.ExceptionAsync(() =>
            dispatcher.DispatchAsync(
                method.WireName,
                payload.Memory,
                serializer,
                registry,
                output,
                source.Token));

        Assert.True(
            exception is OperationCanceledException,
            "Expected OperationCanceledException after deserializer-canceled dispatch token; actual " +
            $"{exception?.GetType().Name ?? "no exception"}, " +
            $"deserialize calls {serializer.DeserializeCalls}, " +
            $"receiver calls {service.CallCount}, " +
            $"serialize calls {serializer.SerializeCalls}, " +
            $"bytes written {output.WrittenCount}.");
        Assert.Equal(1, serializer.DeserializeCalls);
        Assert.Equal(0, service.CallCount);
        Assert.Equal(0, serializer.SerializeCalls);
        Assert.Equal(0, output.WrittenCount);
    }

    private static GeneratedMethod FindMethod(string name) =>
        Assert.Single(
            GeneratedServiceRegistry.GetService<IDispatchCancellationService>().Methods,
            candidate => candidate.Name == name);

    private sealed class RecordingDispatchCancellationService : IDispatchCancellationService
    {
        public int CallCount { get; private set; }

        public int Record(int value)
        {
            CallCount++;
            return value + 1;
        }

        public Task<int> RecordAfterCancelAsync(int value, CancellationToken ct = default) =>
            Task.FromResult(value + 1);

        public void CancelVoid(CancellationToken ct = default)
        {
        }

        public Task CancelTaskAsync(CancellationToken ct = default) =>
            Task.CompletedTask;

        public ValueTask CancelValueTaskAsync(CancellationToken ct = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class ReceiverCancelingDispatchCancellationService(
        CancellationTokenSource source) : IDispatchCancellationService
    {
        public int CallCount { get; private set; }

        public int Record(int value) => value + 1;

        public Task<int> RecordAfterCancelAsync(int value, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
            {
                return Task.FromCanceled<int>(ct);
            }

            CallCount++;
            source.Cancel();
            return Task.FromResult(value + 1);
        }

        public void CancelVoid(CancellationToken ct = default)
        {
            CallCount++;
            source.Cancel();
        }

        public Task CancelTaskAsync(CancellationToken ct = default)
        {
            CallCount++;
            source.Cancel();
            return Task.CompletedTask;
        }

        public ValueTask CancelValueTaskAsync(CancellationToken ct = default)
        {
            CallCount++;
            source.Cancel();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingSerializer(ISerializer inner, Action? beforeDeserialize = null) : ISerializer
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
            beforeDeserialize?.Invoke();
            return inner.Deserialize<T>(data);
        }

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
        {
            DeserializeCalls++;
            beforeDeserialize?.Invoke();
            return inner.Deserialize(data, type);
        }
    }

}

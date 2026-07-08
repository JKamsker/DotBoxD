using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.GeneratedFixtures;
using Xunit;

namespace DotBoxD.Services.Tests.Server.SubServices;

public sealed class GeneratedSubServiceCancellationRegressionTests
{
    [Fact]
    public async Task Generated_dispatcher_does_not_register_sub_service_after_receiver_cancels_token()
    {
        using var cts = new CancellationTokenSource();
        var service = new CancelBeforeReturnRootService(cts);
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<ISubServiceLifecycleRoot>(service);
        var method = Assert.Single(
            GeneratedServiceRegistry.GetService<ISubServiceLifecycleRoot>().Methods,
            static candidate => candidate.Name == nameof(ISubServiceLifecycleRoot.CreateAsync));
        var serializer = new MessagePackRpcSerializer();
        var registry = new InstanceRegistry();
        var output = new ArrayBufferWriter<byte>();

        var exception = await Record.ExceptionAsync(() =>
            dispatcher.DispatchAsync(
                method.WireName,
                ReadOnlyMemory<byte>.Empty,
                serializer,
                registry,
                output,
                cts.Token));

        var registeredChild = TryGetRegisteredChild(serializer, registry, output, out var handle);

        Assert.True(
            exception is OperationCanceledException &&
            service.CallCount == 1 &&
            !registeredChild &&
            service.Child.Disposed &&
            output.WrittenCount == 0,
            "Expected cancellation after the receiver returned to stop sub-service registration and clean up the child; " +
            $"actual exception {exception?.GetType().Name ?? "none"}, " +
            $"receiver calls {service.CallCount}, " +
            $"output bytes {output.WrittenCount}, " +
            $"handle {Format(handle)}, " +
            $"registered child {registeredChild}, " +
            $"child disposed {service.Child.Disposed}.");
    }

    private static bool TryGetRegisteredChild(
        MessagePackRpcSerializer serializer,
        InstanceRegistry registry,
        ArrayBufferWriter<byte> output,
        out ServiceHandle? handle)
    {
        handle = null;
        if (output.WrittenCount == 0)
        {
            return false;
        }

        var decoded = serializer.Deserialize<ServiceHandle>(output.WrittenMemory);
        handle = decoded;
        return registry.TryGet(decoded.ServiceName, decoded.InstanceId, out var instance) &&
            instance is TrackingLifecycleChild;
    }

    private static string Format(ServiceHandle? handle) =>
        handle.HasValue
            ? $"{handle.Value.ServiceName}/{handle.Value.InstanceId}"
            : "<none>";

    private sealed class CancelBeforeReturnRootService(CancellationTokenSource cts) : ISubServiceLifecycleRoot
    {
        public TrackingLifecycleChild Child { get; } = new();

        public int CallCount { get; private set; }

        public Task<ISubServiceLifecycleChild> CreateAsync(CancellationToken ct = default)
        {
            CallCount++;
            cts.Cancel();
            return Task.FromResult<ISubServiceLifecycleChild>(Child);
        }
    }

    private sealed class TrackingLifecycleChild : ISubServiceLifecycleChild
    {
        public bool Disposed { get; private set; }

        public Task<int> PingAsync(CancellationToken ct = default) => Task.FromResult(42);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }
}

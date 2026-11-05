using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.GeneratedFixtures;
using Xunit;

namespace DotBoxD.Services.Tests.Server.SubServices;

public sealed class GeneratedInstanceDispatchWrongTypeLeaseRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task GeneratedDispatcher_ReleasesLeaseWhenRegisteredInstanceHasWrongServiceType()
    {
        var wrongService = new TrackingDisposable();
        var serializer = new MessagePackRpcSerializer();
        var registry = new InstanceRegistry();
        var descriptor = GeneratedServiceRegistry.GetService<ISubServiceLifecycleChild>();
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<ISubServiceLifecycleChild>(new LifecycleChildService());
        var instanceId = registry.Register(descriptor.ServiceName, wrongService);

        var exception = await Assert.ThrowsAsync<ServiceNotFoundException>(() =>
            dispatcher.DispatchOnInstanceAsync(
                instanceId,
                nameof(ISubServiceLifecycleChild.PingAsync),
                ReadOnlyMemory<byte>.Empty,
                serializer,
                registry,
                new ArrayBufferWriter<byte>()));

        Assert.Equal(ServiceNotFoundException.NotFoundKind.Instance, exception.Kind);

        await registry.ReleaseAsync(descriptor.ServiceName, instanceId).AsTask().WaitAsync(Timeout);

        Assert.Equal(1, wrongService.DisposeCalls);
    }

    [Fact]
    public async Task GeneratedDispatcher_DispatchesMatchingRegisteredInstance()
    {
        var service = new LifecycleChildService();
        var serializer = new MessagePackRpcSerializer();
        var registry = new InstanceRegistry();
        var descriptor = GeneratedServiceRegistry.GetService<ISubServiceLifecycleChild>();
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<ISubServiceLifecycleChild>(service);
        var method = Assert.Single(
            descriptor.Methods,
            static candidate => candidate.Name == nameof(ISubServiceLifecycleChild.PingAsync));
        var instanceId = registry.Register(descriptor.ServiceName, service);

        await dispatcher.DispatchOnInstanceAsync(
            instanceId,
            method.WireName,
            ReadOnlyMemory<byte>.Empty,
            serializer,
            registry,
            new ArrayBufferWriter<byte>());

        await registry.ReleaseAsync(descriptor.ServiceName, instanceId).AsTask().WaitAsync(Timeout);

        Assert.Equal(1, service.CallCount);
        Assert.Equal(1, service.DisposeCalls);
    }

    private sealed class TrackingDisposable : IAsyncDisposable
    {
        public int DisposeCalls { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return default;
        }
    }

    private sealed class LifecycleChildService : ISubServiceLifecycleChild
    {
        public int CallCount { get; private set; }

        public int DisposeCalls { get; private set; }

        public Task<int> PingAsync(CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(42);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return default;
        }
    }
}

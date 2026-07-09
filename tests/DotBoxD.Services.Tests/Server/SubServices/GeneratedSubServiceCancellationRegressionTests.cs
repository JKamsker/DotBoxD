using System.Buffers;
using DotBoxD.Codecs.MessagePack;
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
        var registry = new TrackingInstanceRegistry();
        var output = new ArrayBufferWriter<byte>();

        var exception = await Record.ExceptionAsync(() =>
            dispatcher.DispatchAsync(
                method.WireName,
                ReadOnlyMemory<byte>.Empty,
                serializer,
                registry,
                output,
                cts.Token));

        Assert.IsType<OperationCanceledException>(exception);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(0, registry.RegisterCount);
        Assert.True(service.Child.Disposed);
        Assert.Equal(0, output.WrittenCount);
    }

    [Fact]
    public async Task Generated_dispatcher_releases_sub_service_when_registration_cancels_token()
    {
        using var cts = new CancellationTokenSource();
        var service = new RegisterCancellationRootService();
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<ISubServiceLifecycleRoot>(service);
        var method = Assert.Single(
            GeneratedServiceRegistry.GetService<ISubServiceLifecycleRoot>().Methods,
            static candidate => candidate.Name == nameof(ISubServiceLifecycleRoot.CreateAsync));
        var serializer = new MessagePackRpcSerializer();
        var registry = new CancellingInstanceRegistry(cts);
        var output = new ArrayBufferWriter<byte>();

        var exception = await Record.ExceptionAsync(() =>
            dispatcher.DispatchAsync(
                method.WireName,
                ReadOnlyMemory<byte>.Empty,
                serializer,
                registry,
                output,
                cts.Token));

        Assert.IsType<OperationCanceledException>(exception);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(1, registry.RegisterCount);
        Assert.Equal(1, registry.ReleaseAsyncCount);
        Assert.True(service.Child.Disposed);
        Assert.Equal(0, output.WrittenCount);
    }

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

    private sealed class RegisterCancellationRootService : ISubServiceLifecycleRoot
    {
        public TrackingLifecycleChild Child { get; } = new();

        public int CallCount { get; private set; }

        public Task<ISubServiceLifecycleChild> CreateAsync(CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult<ISubServiceLifecycleChild>(Child);
        }
    }

    private abstract class DelegatingInstanceRegistry : IInstanceRegistry
    {
        protected InstanceRegistry Inner { get; } = new();

        public abstract string Register(string serviceName, object instance);

        public bool TryGet(string serviceName, string instanceId, out object instance) =>
            Inner.TryGet(serviceName, instanceId, out instance);

        public void Release(string serviceName, string instanceId) =>
            Inner.Release(serviceName, instanceId);

        public virtual ValueTask ReleaseAsync(string serviceName, string instanceId) =>
            Inner.ReleaseAsync(serviceName, instanceId);

        public void ReleaseAll() => Inner.ReleaseAll();
    }

    private sealed class TrackingInstanceRegistry : DelegatingInstanceRegistry
    {
        public int RegisterCount { get; private set; }

        public override string Register(string serviceName, object instance)
        {
            RegisterCount++;
            return Inner.Register(serviceName, instance);
        }
    }

    private sealed class CancellingInstanceRegistry(CancellationTokenSource cts) : DelegatingInstanceRegistry
    {
        public int RegisterCount { get; private set; }

        public int ReleaseAsyncCount { get; private set; }

        public override string Register(string serviceName, object instance)
        {
            RegisterCount++;
            var id = Inner.Register(serviceName, instance);
            cts.Cancel();
            return id;
        }

        public override async ValueTask ReleaseAsync(string serviceName, string instanceId)
        {
            ReleaseAsyncCount++;
            await Inner.ReleaseAsync(serviceName, instanceId);
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

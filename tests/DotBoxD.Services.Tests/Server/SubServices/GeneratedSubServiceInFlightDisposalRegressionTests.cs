using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.GeneratedFixtures;
using Xunit;

namespace DotBoxD.Services.Tests.Server.SubServices;

public sealed class GeneratedSubServiceInFlightDisposalRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task GeneratedDispatcher_DoesNotDisposeReceiverUntilAdmittedCallCompletes()
    {
        var service = new BlockingLifecycleChildService();
        var serializer = new MessagePackRpcSerializer();
        var registry = new InstanceRegistry();
        var descriptor = GeneratedServiceRegistry.GetService<ISubServiceLifecycleChild>();
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<ISubServiceLifecycleChild>(service);
        var ping = Assert.Single(
            descriptor.Methods,
            static candidate => candidate.Name == nameof(ISubServiceLifecycleChild.PingAsync));
        var instanceId = registry.Register(descriptor.ServiceName, service);
        var pingOutput = new ArrayBufferWriter<byte>();

        var call = dispatcher.DispatchOnInstanceAsync(
            instanceId,
            ping.WireName,
            ReadOnlyMemory<byte>.Empty,
            serializer,
            registry,
            pingOutput);
        await service.PingEntered.Task.WaitAsync(Timeout);

        var dispose = registry.ReleaseAsync(descriptor.ServiceName, instanceId).AsTask();
        Assert.False(service.Disposed.Task.IsCompleted);

        service.AllowPing.SetResult();

        await call.WaitAsync(Timeout);
        await dispose.WaitAsync(Timeout);
        Assert.True(service.Disposed.Task.IsCompleted);
        Assert.False(registry.TryGet(descriptor.ServiceName, instanceId, out _));
    }

    [Fact]
    public async Task GeneratedDispatcher_PreservesReceiverFailureWhenLeaseDisposalAlsoFails()
    {
        var primary = new InvalidOperationException("receiver failed");
        var cleanup = new InvalidOperationException("lease cleanup failed");
        var service = new ThrowingLifecycleChildService(primary, cleanup);
        var serializer = new MessagePackRpcSerializer();
        var registry = new InstanceRegistry();
        var descriptor = GeneratedServiceRegistry.GetService<ISubServiceLifecycleChild>();
        var dispatcher = GeneratedServiceRegistry.CreateDispatcher<ISubServiceLifecycleChild>(service);
        var ping = Assert.Single(
            descriptor.Methods,
            static candidate => candidate.Name == nameof(ISubServiceLifecycleChild.PingAsync));
        var instanceId = registry.Register(descriptor.ServiceName, service);

        var call = dispatcher.DispatchOnInstanceAsync(
            instanceId,
            ping.WireName,
            ReadOnlyMemory<byte>.Empty,
            serializer,
            registry,
            new ArrayBufferWriter<byte>());
        await service.PingEntered.Task.WaitAsync(Timeout);

        await registry.ReleaseAsync(descriptor.ServiceName, instanceId);
        service.AllowPing.SetResult();

        var actual = await Record.ExceptionAsync(() => call.WaitAsync(Timeout));

        Assert.Same(primary, actual);
        Assert.Equal(1, service.DisposeCount);
    }

    private sealed class BlockingLifecycleChildService : ISubServiceLifecycleChild
    {
        public TaskCompletionSource PingEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowPing { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<int> PingAsync(CancellationToken ct = default)
        {
            PingEntered.SetResult();
            await AllowPing.Task.ConfigureAwait(false);

            if (Disposed.Task.IsCompleted)
            {
                throw new ObjectDisposedException(nameof(BlockingLifecycleChildService));
            }

            return 42;
        }

        public ValueTask DisposeAsync()
        {
            Disposed.SetResult();
            return default;
        }
    }

    private sealed class ThrowingLifecycleChildService(Exception primary, Exception cleanup) : ISubServiceLifecycleChild
    {
        public TaskCompletionSource PingEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowPing { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public async Task<int> PingAsync(CancellationToken ct = default)
        {
            PingEntered.SetResult();
            await AllowPing.Task.ConfigureAwait(false);
            throw primary;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            throw cleanup;
        }
    }
}

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
        await service.Disposed.Task.WaitAsync(Timeout);

        service.AllowPing.SetResult();

        await call.WaitAsync(Timeout);
        await dispose.WaitAsync(Timeout);
        Assert.False(registry.TryGet(descriptor.ServiceName, instanceId, out _));
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
}

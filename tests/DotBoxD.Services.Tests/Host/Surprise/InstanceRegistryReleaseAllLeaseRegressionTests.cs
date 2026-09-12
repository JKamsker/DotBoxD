using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class InstanceRegistryReleaseAllLeaseRegressionTests
{
    [Fact]
    public async Task ReleaseAll_DefersDisposalUntilActiveLeaseIsReleased()
    {
        var registry = new InstanceRegistry();
        var instance = new TrackingAsyncDisposable();
        var instanceId = registry.Register("svc", instance);

        Assert.True(registry.TryAcquire("svc", instanceId, out var acquired, out var lease));
        Assert.Same(instance, acquired);

        registry.ReleaseAll();

        Assert.False(registry.TryGet("svc", instanceId, out _));
        Assert.Equal(0, instance.DisposeCalls);

        await lease.DisposeAsync();

        Assert.Equal(1, instance.DisposeCalls);
    }

    [Fact]
    public async Task ReleaseAllAsync_DefersDisposalUntilActiveLeaseIsReleased()
    {
        var registry = new InstanceRegistry();
        var instance = new TrackingAsyncDisposable();
        var instanceId = registry.Register("svc", instance);

        Assert.True(registry.TryAcquire("svc", instanceId, out var acquired, out var lease));
        Assert.Same(instance, acquired);

        await registry.ReleaseAllAsync();

        Assert.False(registry.TryGet("svc", instanceId, out _));
        Assert.Equal(0, instance.DisposeCalls);

        await lease.DisposeAsync();

        Assert.Equal(1, instance.DisposeCalls);
    }

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public int DisposeCalls { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return default;
        }
    }
}

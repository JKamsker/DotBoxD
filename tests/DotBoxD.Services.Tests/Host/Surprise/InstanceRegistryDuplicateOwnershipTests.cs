using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class InstanceRegistryDuplicateOwnershipTests
{
    [Fact]
    public void Release_KeepsDuplicateOwnedInstanceAliveUntilLastRegistrationIsReleased()
    {
        var registry = new InstanceRegistry();
        var instance = new TrackingDisposable();
        var firstId = registry.Register("svc", instance);
        var secondId = registry.Register("svc", instance);

        registry.Release("svc", firstId);

        Assert.True(registry.TryGet("svc", secondId, out var remaining));
        Assert.Same(instance, remaining);
        Assert.Equal(0, instance.DisposeCalls);

        registry.Release("svc", secondId);

        Assert.Equal(1, instance.DisposeCalls);
    }

    [Fact]
    public async Task ReleaseAsync_KeepsDuplicateOwnedInstanceAliveUntilLastRegistrationIsReleased()
    {
        var registry = new InstanceRegistry();
        var instance = new TrackingAsyncDisposable();
        var firstId = registry.Register("svc", instance);
        var secondId = registry.Register("svc", instance);

        await registry.ReleaseAsync("svc", firstId);

        Assert.True(registry.TryGet("svc", secondId, out var remaining));
        Assert.Same(instance, remaining);
        Assert.Equal(0, instance.DisposeCalls);

        await registry.ReleaseAsync("svc", secondId);

        Assert.Equal(1, instance.DisposeCalls);
    }

    [Fact]
    public void ReleaseAll_DisposesDuplicateOwnedInstanceOnce()
    {
        var registry = new InstanceRegistry();
        var instance = new TrackingDisposable();
        registry.Register("first", instance);
        registry.Register("second", instance);

        registry.ReleaseAll();

        Assert.Equal(1, instance.DisposeCalls);
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
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

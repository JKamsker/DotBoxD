using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class InstanceRegistryLeasedReleaseDisposalCompletionTests
{
    [Fact]
    public async Task ReleaseAsync_WaitsForLeasedDisposalAndPropagatesFailure()
    {
        var registry = new InstanceRegistry();
        var expected = new InvalidOperationException("leased async dispose failed");
        var instance = new ThrowingAsyncDisposable(expected);
        var instanceId = registry.Register("svc", instance);
        Assert.True(registry.TryAcquire("svc", instanceId, out _, out var lease));

        var release = registry.ReleaseAsync("svc", instanceId).AsTask();

        Assert.False(release.IsCompleted);
        Assert.False(instance.DisposeCalled);

        var leaseFailure = await Record.ExceptionAsync(() => lease.DisposeAsync().AsTask());
        var releaseFailure = await Record.ExceptionAsync(() => release);

        Assert.Same(expected, leaseFailure);
        Assert.Same(expected, releaseFailure);
        Assert.True(instance.DisposeCalled);
    }

    private sealed class ThrowingAsyncDisposable(Exception error) : IAsyncDisposable
    {
        public bool DisposeCalled { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            throw error;
        }
    }
}

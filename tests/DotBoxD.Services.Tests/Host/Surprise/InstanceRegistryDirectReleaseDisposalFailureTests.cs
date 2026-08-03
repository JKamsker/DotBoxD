using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class InstanceRegistryDirectReleaseDisposalFailureTests
{
    [Fact]
    public void Release_PropagatesDisposalFailureToDirectCaller()
    {
        var registry = new InstanceRegistry();
        var expected = new InvalidOperationException("sync dispose failed");
        var id = registry.Register("svc", new ThrowingDisposable(expected));

        var actual = Assert.Throws<InvalidOperationException>(() => registry.Release("svc", id));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task ReleaseAsync_PropagatesDisposalFailureToDirectCaller()
    {
        var registry = new InstanceRegistry();
        var expected = new InvalidOperationException("async dispose failed");
        var id = registry.Register("svc", new ThrowingAsyncDisposable(expected));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.ReleaseAsync("svc", id).AsTask());

        Assert.Same(expected, actual);
    }

    [Fact]
    public void ReleaseAll_RemainsBestEffortWhenDisposalFails()
    {
        var registry = new InstanceRegistry();
        var expected = new InvalidOperationException("teardown dispose failed");
        registry.Register("svc", new ThrowingDisposable(expected));

        var actual = Record.Exception(registry.ReleaseAll);

        Assert.Null(actual);
    }

    private sealed class ThrowingDisposable(Exception error) : IDisposable
    {
        public void Dispose() => throw error;
    }

    private sealed class ThrowingAsyncDisposable(Exception error) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => throw error;
    }
}

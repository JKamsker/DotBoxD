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
        var first = new ThrowingDisposable(
            new InvalidOperationException("first teardown dispose failed"));
        var second = new ThrowingDisposable(
            new InvalidOperationException("second teardown dispose failed"));
        registry.Register("svc", first);
        registry.Register("svc", second);

        var actual = Record.Exception(registry.ReleaseAll);

        Assert.Null(actual);
        Assert.True(first.DisposeCalled);
        Assert.True(second.DisposeCalled);
    }

    [Fact]
    public async Task ReleaseAllAsync_RemainsBestEffortWhenDisposalFails()
    {
        var registry = new InstanceRegistry();
        var first = new ThrowingAsyncDisposable(
            new InvalidOperationException("async teardown dispose failed"));
        var second = new TrackingAsyncDisposable();
        registry.Register("svc", first);
        registry.Register("svc", second);

        var actual = await Record.ExceptionAsync(registry.ReleaseAllAsync);

        Assert.Null(actual);
        Assert.True(first.DisposeCalled);
        Assert.True(second.DisposeCalled);
    }

    private sealed class ThrowingDisposable(Exception error) : IDisposable
    {
        public bool DisposeCalled { get; private set; }

        public void Dispose()
        {
            DisposeCalled = true;
            throw error;
        }
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

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public bool DisposeCalled { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            return default;
        }
    }
}

using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class InstanceRegistryReleaseAllRaceTests
{
    [Fact]
    public async Task ReleaseAll_DrainsRegistrationPublishedWhileDisposeIsInProgress()
    {
        var registry = new InstanceRegistry();
        var first = new BlockingDisposable();
        registry.Register("svc", first);

        var release = Task.Run(registry.ReleaseAll);
        await first.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = new TrackingDisposable();
        Assert.Throws<InvalidOperationException>(() => registry.Register("svc", second));

        first.AllowDispose.SetResult();
        await release.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(second.Disposed);
        Assert.Throws<InvalidOperationException>(() => registry.Register("svc", second));
    }

    [Fact]
    public async Task ReleaseAllAsync_DrainsRegistrationPublishedWhileDisposeIsInProgress()
    {
        var registry = new InstanceRegistry();
        var first = new BlockingAsyncDisposable();
        registry.Register("svc", first);

        var release = registry.ReleaseAllAsync();
        await first.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = new TrackingAsyncDisposable();
        Assert.Throws<InvalidOperationException>(() => registry.Register("svc", second));

        first.AllowDispose.SetResult();
        await release.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(second.Disposed);
        Assert.Throws<InvalidOperationException>(() => registry.Register("svc", second));
    }

    private sealed class BlockingDisposable : IDisposable
    {
        public TaskCompletionSource DisposeEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowDispose { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
            DisposeEntered.SetResult();
            AllowDispose.Task.GetAwaiter().GetResult();
        }
    }

    private sealed class BlockingAsyncDisposable : IAsyncDisposable
    {
        public TaskCompletionSource DisposeEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowDispose { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            DisposeEntered.SetResult();
            await AllowDispose.Task.ConfigureAwait(false);
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }
}

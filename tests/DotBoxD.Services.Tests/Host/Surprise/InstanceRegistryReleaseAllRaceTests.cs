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
        var secondId = TryRegister(registry, second);

        first.AllowDispose.SetResult();
        await release.WaitAsync(TimeSpan.FromSeconds(5));

        if (secondId is null)
        {
            Assert.False(second.Disposed);
            return;
        }

        Assert.False(registry.TryGet("svc", secondId, out _));
        Assert.True(second.Disposed);
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
        var secondId = TryRegister(registry, second);

        first.AllowDispose.SetResult();
        await release.WaitAsync(TimeSpan.FromSeconds(5));

        if (secondId is null)
        {
            Assert.False(second.Disposed);
            return;
        }

        Assert.False(registry.TryGet("svc", secondId, out _));
        Assert.True(second.Disposed);
    }

    private static string? TryRegister(InstanceRegistry registry, object instance)
    {
        try
        {
            return registry.Register("svc", instance);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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

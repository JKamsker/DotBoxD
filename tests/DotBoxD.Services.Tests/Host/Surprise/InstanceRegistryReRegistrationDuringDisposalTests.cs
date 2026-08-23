using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Host;

public sealed class InstanceRegistryReRegistrationDuringDisposalTests
{
    [Fact]
    public async Task Release_RejectsReregistrationOfInstanceBeingDisposed()
    {
        var registry = new InstanceRegistry();
        var instance = new BlockingDisposable();
        var id = registry.Register("svc", instance);

        var release = Task.Run(() => registry.Release("svc", id));
        await instance.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Assert.Throws<InvalidOperationException>(() => registry.Register("svc", instance));
        }
        finally
        {
            instance.AllowDispose.SetResult();
        }

        await release.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReleaseAsync_RejectsReregistrationOfInstanceBeingDisposed()
    {
        var registry = new InstanceRegistry();
        var instance = new BlockingAsyncDisposable();
        var id = registry.Register("svc", instance);

        var release = registry.ReleaseAsync("svc", id);
        await instance.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Assert.Throws<InvalidOperationException>(() => registry.Register("svc", instance));
        }
        finally
        {
            instance.AllowDispose.SetResult();
        }

        await release.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
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
}

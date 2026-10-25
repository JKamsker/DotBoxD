using System.Collections.Immutable;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.Lifecycle;

public sealed class UiSessionAsyncCleanupReentrancySurpriseTests
{
    [Fact]
    public async Task Async_renderer_cleanup_can_join_its_own_session_disposal()
    {
        using var sandbox = UiTestFixture.Sandbox();
        UiSession? session = null;
        var renderer = new CallbackRenderer(async () =>
        {
            await Task.Yield();
            await session!.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        });
        session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo());
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(session.IsDisconnected);
    }

    [Fact]
    public async Task External_concurrent_disposal_waits_for_and_observes_renderer_failure()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new CallbackRenderer(async () =>
        {
            entered.TrySetResult();
            await release.Task;
            throw new InvalidOperationException("renderer sentinel");
        });
        var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo());
        var first = session.DisposeAsync().AsTask();
        await entered.Task;
        var second = session.DisposeAsync().AsTask();
        try
        { Assert.False(second.IsCompleted); }
        finally { release.TrySetResult(); }
        var firstError = await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        Assert.Same(firstError, await Assert.ThrowsAsync<InvalidOperationException>(() => second));
    }

    [Fact]
    public async Task Detached_renderer_cleanup_observes_terminal_session_failure()
    {
        using var sandbox = UiTestFixture.Sandbox();
        UiSession? session = null;
        Task? detached = null;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new CallbackRenderer(() =>
        {
            detached = Task.Run(async () =>
            {
                await release.Task;
                await session!.DisposeAsync();
            });
            throw new InvalidOperationException("renderer sentinel");
        });
        session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
        release.TrySetResult();
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => detached!.WaitAsync(TimeSpan.FromSeconds(5))));
    }

    private sealed class CallbackRenderer(Func<ValueTask> dispose) : IUiRenderer
    {
        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => dispose();
    }
}

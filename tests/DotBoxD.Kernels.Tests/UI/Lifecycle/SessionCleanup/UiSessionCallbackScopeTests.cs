using System.Collections.Immutable;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.Lifecycle;

public sealed class UiSessionCallbackScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Custom_adapter_callback_captures_scope_after_the_invocation_scope_is_disposed(bool fail)
    {
        using var sandbox = UiTestFixture.Sandbox();
        using var cancellation = new CancellationTokenSource();
        var renderer = new CancellingRenderer(cancellation, fail);
        var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo());
        var joined = false;
        using (session.EnterCallbackScope())
        {
            cancellation.Token.Register(() =>
            {
                Task.Run(async () => await session.DisposeAsync()).WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
                joined = true;
            });
        }
        if (fail)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
            Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask()));
        }
        else
        { await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.True(joined);
    }

    [Fact]
    public async Task Disposed_callback_scope_restores_external_disposal_waiting()
    {
        using var sandbox = UiTestFixture.Sandbox();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellation.Token.Register(() =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            throw new InvalidOperationException("callback sentinel");
        });
        var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), new CancellingRenderer(cancellation, false), TestUiTransport.Echo());
        using (session.EnterCallbackScope())
        { }
        var first = session.DisposeAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = session.DisposeAsync().AsTask();
        try
        { Assert.False(second.IsCompleted); }
        finally { release.TrySetResult(); }
        var error = await Assert.ThrowsAsync<AggregateException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(error, await Assert.ThrowsAsync<AggregateException>(() => second.WaitAsync(TimeSpan.FromSeconds(5))));
    }

    private sealed class CancellingRenderer(CancellationTokenSource cancellation, bool fail) : IUiRenderer
    {
        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync();
            if (fail)
            { throw new InvalidOperationException("renderer sentinel"); }
        }
    }
}

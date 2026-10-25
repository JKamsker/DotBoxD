using System.Collections.Immutable;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.Lifecycle;

public sealed class UiSessionInputCancellationDisposalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposing_session_completes_when_input_cancellation_callback_disposes_the_same_session(bool forward)
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new DisposalJoiningInputRenderer(forward);
        var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo());
        renderer.Session = session;
        renderer.AllowRegistration.SetResult();
        await renderer.Registered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var disposal = session.DisposeAsync().AsTask();
        await renderer.CallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        await renderer.ReadCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await renderer.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(session.IsDisconnected);
        Assert.Equal(1, renderer.Disposals);
    }

    [Fact]
    public async Task Concurrent_external_disposal_waits_for_callback_completion_and_observes_its_failure()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new ThrowingInputRenderer(entered, release, registered);
        var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo());
        await registered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var first = session.DisposeAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = session.DisposeAsync().AsTask();
        try
        { Assert.False(second.IsCompleted); }
        finally { release.TrySetResult(); }
        var firstError = await Assert.ThrowsAsync<AggregateException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
        var secondError = await Assert.ThrowsAsync<AggregateException>(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(firstError, secondError);
        Assert.Equal("callback sentinel", Assert.Single(secondError.InnerExceptions).Message);
    }

    [Fact]
    public async Task Update_cancellation_callback_can_join_disposal_before_an_earlier_completion_callback()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new DisposalJoiningUpdateRenderer();
        var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo());
        renderer.Session = session;
        var dispatch = session.DispatchAsync(1).AsTask();
        await renderer.Registered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAnyAsync<Exception>(() => dispatch);
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.True(renderer.Joined);
    }

    private sealed class DisposalJoiningUpdateRenderer : IUiRenderer
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public UiSession? Session { get; set; }
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Joined { get; private set; }
        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public async ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => _completed.TrySetCanceled(cancellationToken));
            cancellationToken.Register(() => Joined = Session!.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1)));
            Registered.TrySetResult();
            await _completed.Task;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingInputRenderer(TaskCompletionSource entered, TaskCompletionSource release,
        TaskCompletionSource registered) : IUiRenderer, IUiInputSource
    {
        public async ValueTask<UiInput> ReadAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() =>
            {
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                throw new InvalidOperationException("callback sentinel");
            });
            registered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Input unexpectedly completed");
        }
        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DisposalJoiningInputRenderer(bool forward) : IUiRenderer, IUiInputSource
    {
        public TaskCompletionSource AllowRegistration { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CallbackEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public UiSession? Session { get; set; }
        public int Disposals { get; private set; }

        public async ValueTask<UiInput> ReadAsync(CancellationToken cancellationToken)
        {
            await AllowRegistration.Task.WaitAsync(cancellationToken);
            cancellationToken.Register(() =>
            {
                CallbackEntered.TrySetResult();
                var disposal = forward ? Task.Run(async () => await Session!.DisposeAsync()) : Session!.DisposeAsync().AsTask();
                disposal.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
            });
            Registered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The cancelled input read completed unexpectedly.");
            }
            finally { ReadCancelled.TrySetResult(); }
        }

        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposals++;
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}

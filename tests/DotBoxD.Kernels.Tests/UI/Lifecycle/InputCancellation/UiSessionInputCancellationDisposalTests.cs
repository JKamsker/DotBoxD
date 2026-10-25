using System.Collections.Immutable;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.Lifecycle;

public sealed class UiSessionInputCancellationDisposalTests
{
    [Fact]
    public async Task Disposing_session_completes_when_input_cancellation_callback_disposes_the_same_session()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new DisposalJoiningInputRenderer();
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

    private sealed class DisposalJoiningInputRenderer : IUiRenderer, IUiInputSource
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
                Session!.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

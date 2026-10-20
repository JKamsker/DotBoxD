using System.Collections.Immutable;
using System.Reflection;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.Lifecycle;

public sealed class UiRendererAdmissionTests
{
    [Theory]
    [InlineData("Snapshot")]
    [InlineData("Input")]
    [InlineData("Local")]
    [InlineData("Remote")]
    public async Task Renderer_failure_closes_admission_before_queued_operations_can_enter(string operation)
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new FailingUpdateRenderer();
        var remote = TestUiTransport.Echo();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, remote);
        // Hold only cleanup synchronization to deterministically expose the interval between
        // releasing the state gate and disconnecting. All observed operations use public API.
        var cleanupLock = typeof(UiSession).GetField("_disposeLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() =>
        {
            lock (cleanupLock)
            {
                held.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(30)))
                { throw new TimeoutException("Renderer admission proof did not release cleanup synchronization."); }
            }
        });
        Task<UiSnapshot>? failure = null;
        Exception? queuedError = null;
        try
        {
            await held.Task.WaitAsync(TimeSpan.FromSeconds(10));
            failure = session.SetInputAsync(4, UiPropertyId.Text, UiValue.FromString("committed before rendering")).AsTask();
            await renderer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var queued = operation switch
            {
                "Snapshot" => session.SnapshotAsync().AsTask(),
                "Input" => session.SetInputAsync(4, UiPropertyId.Text, UiValue.FromString("escaped admission")).AsTask(),
                "Local" => session.DispatchAsync(1).AsTask(),
                _ => session.DispatchAsync(2).AsTask()
            };
            renderer.Fail.SetResult();
            queuedError = await Record.ExceptionAsync(() => queued.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            renderer.Fail.TrySetResult();
            release.Set();
            await holder.WaitAsync(TimeSpan.FromSeconds(10));
        }
        var rendererError = await Record.ExceptionAsync(() => failure!.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<IOException>(Assert.IsAssignableFrom<Exception>(rendererError).InnerException);
        Assert.IsType<ObjectDisposedException>(queuedError);
        Assert.True(session.IsDisconnected);
        Assert.Equal(1, renderer.Disposals);
        Assert.Equal(0, remote.Calls);
    }

    private sealed class FailingUpdateRenderer : IUiRenderer
    {
        private int _updates;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Fail { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Disposals { get; private set; }
        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public async ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _updates) != 1)
            { return; }
            Entered.SetResult();
            await Fail.Task.ConfigureAwait(false);
            throw new IOException("Renderer partially applied the update.");
        }
        public ValueTask DisposeAsync()
        { Disposals++; return ValueTask.CompletedTask; }
    }
}

using System.Collections.Immutable;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.Lifecycle;

public sealed class UiInputAcknowledgementTests
{
    [Fact]
    public async Task Input_acknowledgement_failure_closes_queued_admission_and_releases_renderer()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new FailingAcknowledgementRenderer();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            renderer.Input.SetResult(new UiInput(NodeId: 4, PropertyId: UiPropertyId.Text, Value: UiValue.FromString("edit")));
            await renderer.Entered.Task.WaitAsync(timeout.Token);
            var queued = session.SnapshotAsync(timeout.Token).AsTask();
            Assert.False(queued.IsCompleted);
            renderer.Fail.SetResult();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
            await renderer.Disposed.Task.WaitAsync(timeout.Token);
            Assert.True(session.IsDisconnected);
            Assert.Equal(1, renderer.Disposals);
        }
        finally { renderer.Fail.TrySetResult(); }
    }

    private sealed class FailingAcknowledgementRenderer : IUiRenderer, IUiInputSource
    {
        private UiInput? _input;
        public TaskCompletionSource<UiInput> Input { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Fail { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Disposals { get; private set; }

        public async ValueTask<UiInput> ReadAsync(CancellationToken cancellationToken)
        {
            if (_input is not null)
            { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            return _input = await Input.Task.WaitAsync(cancellationToken);
        }
        public async ValueTask AcknowledgeAsync(UiInput input, CancellationToken cancellationToken)
        {
            Assert.Same(_input, input);
            Entered.SetResult();
            await Fail.Task.WaitAsync(cancellationToken);
            throw new IOException("Acknowledgement failed after the update.");
        }
        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        public ValueTask DisposeAsync()
        { Disposals++; Disposed.TrySetResult(); return ValueTask.CompletedTask; }
    }
}

using System.Collections.Immutable;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.Lifecycle;

public sealed class UiHostInstallCancellationCleanupTests
{
    [Fact]
    public async Task Renderer_cleanup_failure_does_not_mask_installation_cancellation()
    {
        using var sandbox = UiTestFixture.Sandbox();
        using var caller = new CancellationTokenSource();
        var renderer = new DisposeFailingRenderer();
        caller.Cancel();

        var error = await Record.ExceptionAsync(() => UiTestFixture.Host(sandbox)
            .InstallAsync(UiTestFixture.Counter(), renderer, cancellationToken: caller.Token).AsTask());

        var cancellation = Assert.IsType<OperationCanceledException>(error);
        Assert.Equal(caller.Token, cancellation.CancellationToken);
        Assert.Equal(1, renderer.Disposals);
    }

    private sealed class DisposeFailingRenderer : IUiRenderer
    {
        public int Disposals { get; private set; }

        public ValueTask MaterializeAsync(
            UiPackage package,
            ImmutableArray<UiPropertyValue> values,
            CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposals++;
            throw new IOException("Renderer cleanup failed.");
        }
    }
}

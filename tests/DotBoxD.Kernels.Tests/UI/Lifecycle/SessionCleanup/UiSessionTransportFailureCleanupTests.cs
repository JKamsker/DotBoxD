using System.Collections.Immutable;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.Lifecycle;

public sealed class UiSessionTransportFailureCleanupTests
{
    [Fact]
    public async Task Remote_transport_failure_remains_primary_when_renderer_cleanup_fails()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new FailingDisposeRenderer();
        var transport = new TestUiTransport((_, _) =>
            ValueTask.FromException<UiStatePatch>(new InvalidOperationException("transport failure")));
        var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, transport);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DispatchAsync(2));

        Assert.Equal("transport failure", failure.Message);
        Assert.True(session.IsDisconnected);
        Assert.Equal(1, renderer.Disposals);
    }

    [Fact]
    public async Task Explicit_disposal_still_exposes_renderer_cleanup_failure()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new FailingDisposeRenderer();
        var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo());

        var failure = await Assert.ThrowsAsync<IOException>(async () => await session.DisposeAsync());

        Assert.Equal("renderer cleanup failure", failure.Message);
        Assert.Equal(1, renderer.Disposals);
    }

    private sealed class FailingDisposeRenderer : IUiRenderer
    {
        public int Disposals { get; private set; }

        public ValueTask MaterializeAsync(
            UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask UpdateAsync(
            ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.FromException(new IOException("renderer cleanup failure"));
        }
    }
}

using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI;

public sealed class UiRemoteTransportCancellationPrecedenceSurpriseTests
{
    [Fact]
    public async Task Transport_fault_after_caller_cancellation_cancels_only_the_dispatch()
    {
        using var sandbox = UiTestFixture.Sandbox();
        using var caller = new CancellationTokenSource();
        var renderer = new RecordingUiRenderer();
        var remote = new TestUiTransport((_, _) =>
        {
            caller.Cancel();
            return ValueTask.FromException<UiStatePatch>(new IOException("transport fault"));
        });
        await using var session = await UiTestFixture.Host(sandbox)
            .InstallAsync(UiTestFixture.Counter(), renderer, remote);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.DispatchAsync(2, caller.Token).AsTask());

        Assert.False(session.IsDisconnected);
        Assert.Equal(0, renderer.Disposals);
        Assert.Equal(1, UiTestFixture.Slot(await session.DispatchAsync(1), 1).Integer);
    }

    [Fact]
    public async Task Transport_fault_with_a_live_caller_token_disconnects_the_session()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new RecordingUiRenderer();
        var remote = new TestUiTransport((_, _) =>
            ValueTask.FromException<UiStatePatch>(new IOException("transport fault")));
        await using var session = await UiTestFixture.Host(sandbox)
            .InstallAsync(UiTestFixture.Counter(), renderer, remote);

        await Assert.ThrowsAsync<IOException>(() => session.DispatchAsync(2).AsTask());

        Assert.True(session.IsDisconnected);
        Assert.Equal(1, renderer.Disposals);
    }
}

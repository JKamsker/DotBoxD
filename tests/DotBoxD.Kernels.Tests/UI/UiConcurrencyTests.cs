using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI;

public sealed class UiConcurrencyTests
{
    [Fact]
    public async Task Remote_deadline_disconnects_noncooperative_transport_and_disposes_renderer()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var never = new TaskCompletionSource<UiStatePatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new TestUiTransport((_, _) => new ValueTask<UiStatePatch>(never.Task));
        var renderer = new RecordingUiRenderer();
        await using var session = await UiTestFixture.Host(sandbox, new UiPolicy { RemoteEventTimeout = TimeSpan.FromMilliseconds(200) })
            .InstallAsync(UiTestFixture.Counter(), renderer, remote);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.DispatchAsync(2).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(session.IsDisconnected);
        Assert.Equal(1, renderer.Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await session.SnapshotAsync());
        Assert.Equal(1, remote.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_leaves_the_session_usable_and_releases_remote_admission()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var remote = new TestUiTransport(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("The cancellable remote call must not complete normally.");
        });
        await using var session = await UiTestFixture.Host(sandbox, new UiPolicy { MaxInFlightRemoteEvents = 1 })
            .InstallAsync(UiTestFixture.Counter(), new RecordingUiRenderer(), remote);
        using var caller = new CancellationTokenSource();
        var pending = session.DispatchAsync(2, caller.Token).AsTask();
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(session.IsDisconnected);
        Assert.Equal(1, UiTestFixture.Slot(await session.DispatchAsync(1), 1).Integer);
    }

    [Fact]
    public async Task Local_edit_during_remote_call_rejects_the_stale_reply()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var started = new TaskCompletionSource<UiRemoteEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<UiStatePatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new TestUiTransport((request, _) => { started.SetResult(request); return new(reply.Task); });
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), new RecordingUiRenderer(), remote);
        var pending = session.DispatchAsync(2).AsTask();
        var message = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await session.SetInputAsync(4, UiPropertyId.Text, UiValue.FromString("new query"));
        reply.SetResult(UiTestFixture.Reply(message, new UiStateValue(3, UiValue.FromString("stale"))));
        await Assert.ThrowsAsync<UiValidationException>(() => pending);
        Assert.Equal("new query", UiTestFixture.Slot(await session.SnapshotAsync(), 2).Text);
        Assert.Empty(UiTestFixture.Slot(await session.SnapshotAsync(), 3).Text);
    }

    [Fact]
    public async Task Remote_reply_cannot_rebase_itself_onto_a_newer_version()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var remote = new TestUiTransport((request, _) => ValueTask.FromResult(
            new UiStatePatch(request.Snapshot.SessionId, request.Snapshot.Version + 1, [])));
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), new RecordingUiRenderer(), remote);
        await Assert.ThrowsAsync<UiValidationException>(async () => await session.DispatchAsync(2));
        Assert.Equal(0, (await session.SnapshotAsync()).Version);
    }

    [Fact]
    public async Task Concurrent_local_events_are_serialized_without_lost_updates()
    {
        using var sandbox = UiTestFixture.Sandbox();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), new RecordingUiRenderer(), TestUiTransport.Echo());
        await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(async () => await session.DispatchAsync(1))));
        var snapshot = await session.SnapshotAsync();
        Assert.Equal(30, snapshot.Version);
        Assert.Equal(30, UiTestFixture.Slot(snapshot, 1).Integer);
    }

    [Fact]
    public async Task In_flight_remote_events_are_bounded_and_teardown_cancels_noncooperative_calls()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remote = new TestUiTransport((_, _) =>
        {
            started.TrySetResult();
            return new(new TaskCompletionSource<UiStatePatch>(TaskCreationOptions.RunContinuationsAsynchronously).Task);
        });
        var renderer = new RecordingUiRenderer();
        var session = await UiTestFixture.Host(sandbox, new UiPolicy { MaxInFlightRemoteEvents = 1 })
            .InstallAsync(UiTestFixture.Counter(), renderer, remote);
        var pending = session.DispatchAsync(2).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<UiValidationException>(async () => await session.DispatchAsync(2));
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(session.IsDisconnected);
        Assert.Equal(1, renderer.Disposals);
    }
}

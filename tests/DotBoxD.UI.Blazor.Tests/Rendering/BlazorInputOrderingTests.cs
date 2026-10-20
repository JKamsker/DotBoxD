using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor.Tests.Rendering;

public sealed class BlazorInputOrderingTests
{
    [Fact]
    public async Task Earlier_echoes_and_rejection_corrections_do_not_overwrite_newer_queued_browser_edits()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox, new UiPolicy { MaxStringLength = 4 }).InstallAsync(UiFixture.TextPackage(), renderer);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var subscription = renderer.Subscribe(async () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            { entered.TrySetResult(); await release.Task; }
        });
        UiInput Input(string value) => new(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromString(value));
        try
        {
            await renderer.SubmitAsync(session, Input("old"), UiFixture.User(), new Authorizer());
            await entered.Task;
            await renderer.SubmitAsync(session, Input("too long"), UiFixture.User(), new Authorizer());
            await renderer.SubmitAsync(session, Input("new"), UiFixture.User(), new Authorizer());
            Assert.Equal("new", renderer.Snapshot!.Get(1, UiPropertyId.Text)!.Text);
        }
        finally { release.TrySetResult(); }
        await UiFixture.WaitAsync(async () => (await session.SnapshotAsync()).State[0].Value.Text == "new");
        Assert.Equal("new", renderer.Snapshot!.Get(1, UiPropertyId.Text)!.Text);
        Assert.NotNull(session.LastInputError);
        Assert.False(session.IsDisconnected);
    }

    [Fact]
    public async Task Async_authorization_preserves_browser_input_order()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        var authorizer = new DelayedAuthorizer();
        UiInput Input(string value) => new(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromString(value));
        var first = renderer.SubmitAsync(session, Input("old"), UiFixture.User(), authorizer).AsTask();
        await authorizer.Entered.Task;
        var second = renderer.SubmitAsync(session, Input("new"), UiFixture.User(), authorizer).AsTask();
        try
        { Assert.Equal(1, authorizer.Calls); }
        finally { authorizer.Release.TrySetResult(true); }
        Assert.True(await first);
        Assert.True(await second);
        await UiFixture.WaitAsync(async () => (await session.SnapshotAsync()).Version == 2);
        Assert.Equal("new", (await session.SnapshotAsync()).State[0].Value.Text);
    }

    [Fact]
    public async Task Queue_overflow_disconnects_and_releases_viewer_without_unbounded_input()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer(inputCapacity: 1);
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = renderer.Subscribe(async () => { entered.TrySetResult(); await release.Task; });
        UiInput Input(string value) => new(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromString(value));
        try
        {
            await renderer.SubmitAsync(session, Input("one"), UiFixture.User(), new Authorizer());
            await entered.Task;
            await renderer.SubmitAsync(session, Input("two"), UiFixture.User(), new Authorizer());
            await Assert.ThrowsAsync<UiValidationException>(() => renderer.SubmitAsync(session, Input("three"), UiFixture.User(), new Authorizer()).AsTask());
        }
        finally { release.TrySetResult(); }
        await UiFixture.WaitAsync(() => Task.FromResult(session.IsDisconnected));
        await session.DisposeAsync();
        Assert.True(renderer.IsDisposed);
        Assert.Null(renderer.Snapshot);
    }

    private sealed class DelayedAuthorizer : IUiInteractionAuthorizer
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<bool> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, UiSession session, UiInput input, CancellationToken cancellationToken)
        {
            if (++Calls == 1)
            { Entered.TrySetResult(); return new ValueTask<bool>(Release.Task); }
            return ValueTask.FromResult(true);
        }
    }
}

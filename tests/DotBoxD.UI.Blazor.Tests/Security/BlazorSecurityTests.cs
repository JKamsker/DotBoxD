using DotBoxD.UI.Conformance;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor.Tests.Security;

public sealed class BlazorSecurityTests
{
    [Fact]
    public async Task Browser_identity_authorization_and_session_ownership_are_independent_of_package_ids()
    {
        using var sandbox = UiFixture.Sandbox();
        var firstRenderer = new BlazorUiRenderer();
        var otherRenderer = new BlazorUiRenderer();
        await using var first = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), firstRenderer);
        await using var other = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), otherRenderer);
        var input = new UiInput(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromString("query"));
        var denied = new Authorizer(false);
        var user = UiFixture.User();
        Assert.False(await firstRenderer.SubmitAsync(first, input, user, denied));
        Assert.Same(user, denied.Principal);
        Assert.Same(first, denied.Session);
        Assert.Equal(0, (await first.SnapshotAsync()).Version);
        await Assert.ThrowsAsync<UiValidationException>(() => firstRenderer.SubmitAsync(other, input, user, new Authorizer()).AsTask());
        await Assert.ThrowsAsync<UiValidationException>(() => first.ApplyPatchAsync(new UiStatePatch(other.Id, 0, [])).AsTask());
        Assert.True(await firstRenderer.SubmitAsync(first, input, user, new Authorizer()));
        await UiFixture.WaitAsync(async () => (await first.SnapshotAsync()).Version == 1);
        Assert.Equal("", (await other.SnapshotAsync()).State[0].Value.Text);
        await first.DisposeAsync();
        await Assert.ThrowsAsync<UiValidationException>(() => firstRenderer.SubmitAsync(first, input, user, new Authorizer()).AsTask());
    }

    [Fact]
    public async Task Malformed_unknown_overlong_and_nonfinite_inputs_fail_before_authorization()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer(new UiPolicy { MaxStringLength = 4 });
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        var authorizer = new Authorizer();
        UiInput[] malformed =
        [new(EventId: 123), new(NodeId: 999, PropertyId: UiPropertyId.Text, Value: UiValue.FromString("x")),
         new(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromString("12345")),
         new(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromNumber(double.NaN)),
         new(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromBoolean(true)),
         new(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromString("\ud800")),
         new(EventId: 1, NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromString("x"))];
        foreach (var input in malformed)
        { await Assert.ThrowsAsync<UiValidationException>(() => renderer.SubmitAsync(session, input, UiFixture.User(), authorizer).AsTask()); }
        Assert.Equal(0, authorizer.Calls);
        Assert.Equal(0, (await session.SnapshotAsync()).Version);
    }

    [Fact]
    public async Task Rate_and_authorization_concurrency_are_bounded_even_when_authorizer_ignores_cancellation()
    {
        using var sandbox = UiFixture.Sandbox();
        var policy = new UiPolicy { MaxInputEventsPerSecond = 2, MaxInFlightRemoteEvents = 1 };
        var renderer = new BlazorUiRenderer(policy);
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        var blocked = new BlockingAuthorizer();
        var input = new UiInput(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromString("x"));
        var pending = renderer.SubmitAsync(session, input, UiFixture.User(), blocked).AsTask();
        await blocked.Entered.Task;
        await Assert.ThrowsAsync<UiValidationException>(() => renderer.SubmitAsync(session, input, UiFixture.User(), new Authorizer()).AsTask());
        await session.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        blocked.Reply.TrySetResult(true);
        Assert.Null(renderer.Snapshot);

        var rateRenderer = new BlazorUiRenderer(new UiPolicy { MaxInputEventsPerSecond = 1 });
        await using var rateSession = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), rateRenderer);
        Assert.False(await rateRenderer.SubmitAsync(rateSession, input, UiFixture.User(), new Authorizer(false)));
        await Assert.ThrowsAsync<UiValidationException>(() => rateRenderer.SubmitAsync(rateSession, input, UiFixture.User(), new Authorizer(false)).AsTask());
    }

    [Fact]
    public async Task Disposed_remote_dispatch_cancels_work_and_late_patch_cannot_revive_session()
    {
        using var sandbox = UiFixture.Sandbox();
        var transport = new BlockedTransport();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiRendererConformance.Package(), renderer, transport);
        var dispatch = session.DispatchAsync(2).AsTask();
        await transport.Entered.Task;
        await session.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch);
        Assert.True(transport.Token.IsCancellationRequested);
        transport.Reply.TrySetResult(new UiStatePatch(session.Id, 0, []));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.ApplyPatchAsync(new UiStatePatch(session.Id, 0, [])).AsTask());
        Assert.Null(renderer.Snapshot);
    }

    private sealed class BlockingAuthorizer : IUiInteractionAuthorizer
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<bool> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, UiSession session, UiInput input, CancellationToken cancellationToken)
        { Entered.TrySetResult(); return new ValueTask<bool>(Reply.Task); }
    }
    private sealed class BlockedTransport : IUiRemoteTransport
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<UiStatePatch> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public ValueTask<UiStatePatch> DispatchAsync(UiRemoteEvent message, CancellationToken cancellationToken)
        { Token = cancellationToken; Entered.TrySetResult(); return new ValueTask<UiStatePatch>(Reply.Task); }
    }
}

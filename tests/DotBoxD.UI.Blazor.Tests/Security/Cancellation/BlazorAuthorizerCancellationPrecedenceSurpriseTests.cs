using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor.Tests.Security;

public sealed class BlazorAuthorizerCancellationPrecedenceSurpriseTests
{
    [Fact]
    public async Task Authorizer_fault_after_caller_cancellation_surfaces_cancellation_without_committing_input()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        using var callerCancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.SubmitAsync(
            session, Input(), UiFixture.User(), new CancellingAuthorizer(callerCancellation, fault: true), callerCancellation.Token).AsTask());

        await AssertNotCommittedAsync(session);
    }

    [Fact]
    public async Task Authorizer_denial_after_caller_cancellation_surfaces_cancellation_without_committing_input()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        using var callerCancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.SubmitAsync(
            session, Input(), UiFixture.User(), new CancellingAuthorizer(callerCancellation, fault: false), callerCancellation.Token).AsTask());

        await AssertNotCommittedAsync(session);
    }

    [Fact]
    public async Task Authorizer_fault_with_a_live_caller_token_remains_an_ordinary_fault()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        using var callerCancellation = new CancellationTokenSource();

        await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.SubmitAsync(
            session, Input(), UiFixture.User(), new FaultingAuthorizer(), callerCancellation.Token).AsTask());

        await AssertNotCommittedAsync(session);
    }

    private static UiInput Input() => new(NodeId: 1, PropertyId: UiPropertyId.Text, Value: UiValue.FromString("cancelled"));

    private static async Task AssertNotCommittedAsync(UiSession session)
    {
        await Task.Delay(100);
        Assert.Equal(0, (await session.SnapshotAsync()).Version);
    }

    private sealed class CancellingAuthorizer(CancellationTokenSource callerCancellation, bool fault) : IUiInteractionAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, UiSession session, UiInput input, CancellationToken cancellationToken)
        {
            callerCancellation.Cancel();
            return fault ? ValueTask.FromException<bool>(new InvalidOperationException("authorizer failed")) : ValueTask.FromResult(false);
        }
    }

    private sealed class FaultingAuthorizer : IUiInteractionAuthorizer
    {
        public ValueTask<bool> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, UiSession session, UiInput input, CancellationToken cancellationToken)
            => ValueTask.FromException<bool>(new InvalidOperationException("authorizer failed"));
    }
}

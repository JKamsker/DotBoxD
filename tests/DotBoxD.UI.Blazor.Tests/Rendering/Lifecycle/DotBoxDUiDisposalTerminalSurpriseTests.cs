using System.Security.Claims;
using DotBoxD.UI.Runtime;
using Microsoft.AspNetCore.Components;

namespace DotBoxD.UI.Blazor.Tests.Rendering.Lifecycle;

public sealed class DotBoxDUiDisposalTerminalSurpriseTests
{
    [Fact]
    public async Task Concurrent_disposal_waits_for_component_cancellation_and_owned_session_teardown()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        await using var browser = new TestBlazorRenderer();
        var authorizer = new CancellationBlockingAuthorizer();
        await browser.MountAsync(session, authorizer);

        var interaction = browser.SendAsync("oninput", new ChangeEventArgs { Value = "pending" });
        await authorizer.Entered.Task;
        var firstDisposal = browser.DisposeComponentAsync().AsTask();
        await authorizer.CancellationEntered.Task;
        var concurrentDisposal = browser.DisposeComponentAsync().AsTask();

        try
        {
            Assert.False(firstDisposal.IsCompleted);
            Assert.False(concurrentDisposal.IsCompleted);
            Assert.False(session.IsDisconnected);
        }
        finally { authorizer.ReleaseCancellation.TrySetResult(); }

        await firstDisposal;
        await concurrentDisposal;
        await interaction;
        Assert.True(session.IsDisconnected);
        Assert.Empty(browser.Errors);
    }

    [Fact]
    public async Task Throwing_cancellation_callback_does_not_skip_owned_session_teardown()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        var browser = new TestBlazorRenderer();
        var authorizer = new ThrowingAuthorizer();
        await browser.MountAsync(session, authorizer);
        var interaction = browser.SendAsync("oninput", new ChangeEventArgs { Value = "pending" });
        await authorizer.Registered.Task;
        try
        {
            var error = await Assert.ThrowsAsync<AggregateException>(() => browser.DisposeComponentAsync().AsTask());
            Assert.Contains(error.Flatten().InnerExceptions, e => e.Message == "component cancellation sentinel");
            Assert.True(session.IsDisconnected);
            await interaction;
        }
        finally
        {
            try
            { await browser.DisposeAsync(); }
            catch (AggregateException) { }
            await session.DisposeAsync();
        }
    }

    private sealed class ThrowingAuthorizer : IUiInteractionAuthorizer
    {
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<bool> AuthorizeAsync(ClaimsPrincipal user, UiSession session, UiInput input, CancellationToken cancellationToken)
        {
            cancellationToken.Register(static () => throw new InvalidOperationException("component cancellation sentinel"));
            Registered.TrySetResult();
            return new ValueTask<bool>(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(
                static _ => false, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
        }
    }

    private sealed class CancellationBlockingAuthorizer : IUiInteractionAuthorizer
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<bool> AuthorizeAsync(ClaimsPrincipal user, UiSession session, UiInput input, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            using var registration = cancellationToken.Register(() =>
            {
                CancellationEntered.TrySetResult();
                ReleaseCancellation.Task.GetAwaiter().GetResult();
            });
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        }
    }
}

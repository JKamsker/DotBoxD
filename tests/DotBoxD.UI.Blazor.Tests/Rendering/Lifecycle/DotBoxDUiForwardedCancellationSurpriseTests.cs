using System.Security.Claims;
using DotBoxD.UI.Runtime;
using Microsoft.AspNetCore.Components;

namespace DotBoxD.UI.Blazor.Tests.Rendering.Lifecycle;

public sealed class DotBoxDUiForwardedCancellationSurpriseTests
{
    [Fact]
    public async Task Forwarded_component_disposal_from_cancellation_callback_completes_without_self_join()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        await using var browser = new TestBlazorRenderer();
        var authorizer = new ForwardingAuthorizer(browser);
        await browser.MountAsync(session, authorizer);

        var interaction = browser.SendAsync("oninput", new ChangeEventArgs { Value = "pending" });
        await authorizer.Registered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposal = browser.DisposeComponentAsync().AsTask();
        var completed = await Task.WhenAny(disposal, Task.Delay(TimeSpan.FromSeconds(5)));

        try
        {
            Assert.Same(disposal, completed);
            Assert.True(authorizer.Joined, "The forwarded component disposal did not complete during cancellation.");
            await disposal;
            await interaction;
            Assert.True(session.IsDisconnected);
        }
        finally
        {
            try
            { await disposal.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) when (disposal.IsFaulted || disposal.IsCanceled) { }
            try
            { await interaction.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) when (interaction.IsFaulted || interaction.IsCanceled) { }
            await session.DisposeAsync();
        }
    }

    private sealed class ForwardingAuthorizer(TestBlazorRenderer browser) : IUiInteractionAuthorizer
    {
        public TaskCompletionSource Registered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Joined { get; private set; }

        public ValueTask<bool> AuthorizeAsync(ClaimsPrincipal user, UiSession session, UiInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.Register(() =>
            {
                var forwarded = Task.Run(async () => await browser.DisposeComponentAsync());
                Joined = forwarded.Wait(TimeSpan.FromSeconds(1));
            });
            Registered.TrySetResult();
            return new ValueTask<bool>(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(
                static _ => false, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
        }
    }
}

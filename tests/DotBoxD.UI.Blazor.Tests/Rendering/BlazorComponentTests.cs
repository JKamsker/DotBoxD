using System.Collections.Immutable;
using DotBoxD.UI.Authoring;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotBoxD.UI.Blazor.Tests.Rendering;

public sealed class BlazorComponentTests
{
    [Fact]
    public async Task Every_primitive_renders_trusted_html_and_escapes_plugin_text()
    {
        var b = new UiBuilder();
        var text = b.State("<script>alert('x')</script>");
        var check = b.State(true);
        var number = b.State(20.0);
        var list = b.State(ImmutableArray.Create(new UiListItem("<key>", "<img src=x onerror=alert(1)>")));
        var package = b.Build(b.Border(b.Scroll(b.Stack(b.Grid(2, b.Text(text), b.Text("Value")),
            b.TextBox(text), b.CheckBox("Toggle", check), b.Slider(number), b.Progress(number), b.Items(list))), 4));
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(package, renderer);
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var html = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var output = await html.Dispatcher.InvokeAsync(async () =>
        {
            var root = await html.RenderComponentAsync<DotBoxDUi>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(DotBoxDUi.Session)] = session }));
            return root.ToHtmlString();
        });
        Assert.DoesNotContain("<script>", output, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", output, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", output, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns:repeat(2", output, StringComparison.Ordinal);
        Assert.Contains("padding:4px", output, StringComparison.Ordinal);
        Assert.Contains("overflow:auto", output, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", output, StringComparison.Ordinal);
        Assert.Contains("type=\"range\"", output, StringComparison.Ordinal);
        Assert.Contains("<progress", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Browser_callbacks_update_two_way_state_without_reinstalling_and_correct_rejected_values()
    {
        var policy = new UiPolicy { MaxStringLength = 4 };
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer(policy);
        await using var session = await UiFixture.Host(sandbox, policy).InstallAsync(UiFixture.TextPackage(), renderer);
        await using var browser = new TestBlazorRenderer();
        var authorizer = new Authorizer();
        await browser.MountAsync(session, authorizer);
        await browser.SendAsync("oninput", new ChangeEventArgs { Value = "four" });
        await UiFixture.WaitAsync(async () => (await session.SnapshotAsync()).State[0].Value.Text == "four");
        await browser.SendAsync("oninput", new ChangeEventArgs { Value = "too long" });
        Assert.Equal("four", (await session.SnapshotAsync()).State[0].Value.Text);
        Assert.Equal(1, renderer.Materializations);
        Assert.Equal(1, authorizer.Calls);
        var frames = await browser.FramesAsync();
        Assert.Contains(frames, f => f.FrameType == RenderTreeFrameType.Attribute && f.AttributeName == "value" && Equals(f.AttributeValue, "four"));
        Assert.Empty(browser.Errors);
    }

    [Fact]
    public async Task Checkbox_and_slider_callbacks_use_typed_values_and_missing_authorizer_denies_input()
    {
        var b = new UiBuilder();
        var check = b.State(false);
        var value = b.State(0.0);
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(b.Build(b.Stack(b.CheckBox("Check", check), b.Slider(value))), renderer);
        await using var browser = new TestBlazorRenderer();
        await browser.MountAsync(session, new Authorizer());
        await browser.SendAsync("onchange", new ChangeEventArgs { Value = true });
        await browser.SendAsync("oninput", new ChangeEventArgs { Value = "37.5" });
        await UiFixture.WaitAsync(async () => (await session.SnapshotAsync()).State[1].Value.Number == 37.5);
        Assert.True((await session.SnapshotAsync()).State[0].Value.Boolean);
        await using var denied = new TestBlazorRenderer();
        var other = new BlazorUiRenderer();
        await using var otherSession = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), other);
        await denied.MountAsync(otherSession);
        await denied.SendAsync("oninput", new ChangeEventArgs { Value = "denied" });
        Assert.Equal(0, (await otherSession.SnapshotAsync()).Version);
    }

    [Fact]
    public async Task Keyed_diff_moves_existing_rows_and_parent_visibility_overrides_layout_display()
    {
        var b = new UiBuilder();
        var items = b.State(ImmutableArray.Create(new UiListItem("a", "Alpha"), new UiListItem("b", "Beta")));
        var visible = b.State(true);
        var list = b.Items(items);
        var root = b.Element(UiPrimitive.Stack, [((UiBinding<bool>)visible).Property(UiPropertyId.Visible)], list);
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(b.Build(root), renderer);
        await using var browser = new TestBlazorRenderer();
        await browser.MountAsync(session);
        await session.ApplyPatchAsync(new UiStatePatch(session.Id, 0,
            [new(items.Id, UiValue.FromItems([new("b", "Updated"), new("a", "Alpha")])), new(visible.Id, UiValue.FromBoolean(false))]));
        var frames = await browser.FramesAsync();
        Assert.True(browser.MovedKeyedRows);
        Assert.Contains(frames, f => f.FrameType == RenderTreeFrameType.Element && Equals(f.ElementKey, "b"));
        Assert.Contains(frames, f => f.FrameType == RenderTreeFrameType.Attribute && f.AttributeName == "style" &&
            f.AttributeValue is string style && style.Contains("display:none", StringComparison.Ordinal));
        await session.ApplyPatchAsync(new UiStatePatch(session.Id, 1,
            [new(items.Id, UiValue.FromItems([new("b", "Updated"), new("c", "Charlie")]))]));
        var inserted = await browser.FramesAsync();
        Assert.Equal(new[] { "b", "c" }, inserted.Where(f => f.FrameType == RenderTreeFrameType.Element && f.ElementKey is string)
            .Select(f => (string)f.ElementKey));
        Assert.Equal(1, renderer.Materializations);
    }

    [Fact]
    public async Task Worker_disconnect_during_authorization_does_not_fault_the_Blazor_circuit()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        await using var browser = new TestBlazorRenderer();
        var authorizer = new DeferredAuthorizer();
        await browser.MountAsync(session, authorizer);
        var input = browser.SendAsync("oninput", new ChangeEventArgs { Value = "pending" });
        await authorizer.Entered.Task;
        await session.DisposeAsync();
        await input;
        authorizer.Reply.TrySetResult(true);
        Assert.Empty(browser.Errors);
        Assert.Null(renderer.Snapshot);
    }

    [Fact]
    public async Task Explicit_host_ownership_allows_detach_and_reattach_to_the_same_session()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        await using var first = new TestBlazorRenderer();
        await first.MountAsync(session, new Authorizer(), dispose: false);
        await first.DisposeAsync();
        Assert.False(session.IsDisconnected);
        await using var second = new TestBlazorRenderer();
        await second.MountAsync(session, new Authorizer(), dispose: false);
        await second.SendAsync("oninput", new ChangeEventArgs { Value = "reconnected" });
        await UiFixture.WaitAsync(async () => (await session.SnapshotAsync()).State[0].Value.Text == "reconnected");
        Assert.Equal(1, renderer.Materializations);
    }

    [Fact]
    public async Task Component_replacement_and_disposal_release_sessions_and_subscriptions()
    {
        using var sandbox = UiFixture.Sandbox();
        var first = new BlazorUiRenderer();
        var oldSession = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), first);
        var second = new BlazorUiRenderer();
        var fresh = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), second);
        await using var browser = new TestBlazorRenderer();
        await browser.MountAsync(oldSession, new Authorizer());
        await browser.ReplaceAsync(fresh, new Authorizer());
        Assert.True(oldSession.IsDisconnected);
        Assert.Null(first.Snapshot);
        Assert.NotEqual(oldSession.Id, fresh.Id);
        await browser.DisposeAsync();
        Assert.True(fresh.IsDisconnected);
        Assert.Null(second.Snapshot);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Pending_session_replacement_cannot_attach_after_disposal_or_a_newer_replacement(bool dispose)
    {
        using var sandbox = UiFixture.Sandbox();
        var oldRenderer = new BlazorUiRenderer();
        await using var oldSession = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), oldRenderer);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var blocker = oldRenderer.Subscribe(async () => { entered.TrySetResult(); await release.Task; });
        var update = oldSession.SetInputAsync(1, UiPropertyId.Text, UiValue.FromString("pending")).AsTask();
        await entered.Task;
        blocker.Dispose();
        var nextRenderer = new BlazorUiRenderer();
        await using var next = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), nextRenderer);
        var finalRenderer = new BlazorUiRenderer();
        await using var final = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), finalRenderer);
        await using var browser = new TestBlazorRenderer();
        await browser.MountAsync(oldSession, new Authorizer());
        var replacement = browser.ReplaceAsync(next, new Authorizer());
        Task? followup = null;
        try
        {
            await UiFixture.WaitAsync(() => Task.FromResult(oldSession.IsDisconnected));
            if (dispose)
            { await browser.DisposeAsync(); }
            else
            {
                followup = browser.ReplaceAsync(final, new Authorizer());
                await browser.FramesAsync();
            }
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<ObjectDisposedException>(() => update);
        await replacement;
        if (followup is not null)
        { await followup; }
        Assert.True(next.IsDisconnected);
        Assert.Empty(browser.Errors);
        if (dispose)
        { Assert.Null(nextRenderer.Snapshot); }
        else
        { Assert.False(final.IsDisconnected); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Old_interaction_completion_preserves_replacement_interaction_errors(bool dispose, bool oldAllowed)
    {
        using var sandbox = UiFixture.Sandbox();
        await using var oldSession = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), new BlazorUiRenderer());
        await using var fresh = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), new BlazorUiRenderer());
        await using var browser = new TestBlazorRenderer();
        var authorizer = new DeferredAuthorizer();
        await browser.MountAsync(oldSession, authorizer, dispose);
        var input = browser.SendAsync("oninput", new ChangeEventArgs { Value = "old" });
        await authorizer.Entered.Task;
        await browser.ReplaceAsync(fresh, new Authorizer(!oldAllowed), dispose);
        await browser.SendAsync("oninput", new ChangeEventArgs { Value = "fresh" });
        authorizer.Reply.TrySetResult(oldAllowed);
        await input;
        var frames = await browser.FramesAsync();
        Assert.DoesNotContain(frames, f => f.FrameType == RenderTreeFrameType.Text && f.TextContent == "Plugin disconnected.");
        Assert.Equal(oldAllowed, frames.Any(f => f.FrameType == RenderTreeFrameType.Text && f.TextContent == "This interaction is not authorized."));
        Assert.False(fresh.IsDisconnected);
        Assert.Empty(browser.Errors);
    }

    [Fact]
    public async Task Reattaching_a_retained_session_does_not_accept_its_previous_attachment_completion()
    {
        using var sandbox = UiFixture.Sandbox();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), new BlazorUiRenderer());
        await using var middle = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), new BlazorUiRenderer());
        await using var browser = new TestBlazorRenderer();
        var authorizer = new DeferredAuthorizer();
        await browser.MountAsync(session, authorizer, dispose: false);
        var input = browser.SendAsync("oninput", new ChangeEventArgs { Value = "old" });
        await authorizer.Entered.Task;
        await browser.ReplaceAsync(middle, new Authorizer(), dispose: false);
        await browser.ReplaceAsync(session, null, dispose: false);
        await browser.SendAsync("oninput", new ChangeEventArgs { Value = "denied" });
        authorizer.Reply.TrySetResult(true);
        await input;
        var frames = await browser.FramesAsync();
        Assert.Contains(frames, f => f.FrameType == RenderTreeFrameType.Text && f.TextContent == "This interaction is not authorized.");
        Assert.Empty(browser.Errors);
    }

    private sealed class DeferredAuthorizer : IUiInteractionAuthorizer
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<bool> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, Runtime.UiSession session,
            Runtime.UiInput input, CancellationToken cancellationToken)
        { Entered.TrySetResult(); return new ValueTask<bool>(Reply.Task); }
    }
}

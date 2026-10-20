using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.UI.Authoring;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Avalonia.Tests.Rendering;

public sealed class AvaloniaInputLifecycleTests
{
    [AvaloniaFact]
    public async Task Pending_remote_input_allows_local_edit_and_reports_stale_reply()
    {
        var b = new UiBuilder();
        var text = b.State("");
        var renderer = new AvaloniaUiRenderer();
        var transport = new PendingTransport();
        using var sandbox = SandboxHost.Create(h => h.AddDefaultPureBindings());
        await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build())
            .InstallAsync(b.Build(b.Stack(b.TextBox(text), b.RemoteButton("Search", 7))), renderer, transport);
        var panel = Assert.IsType<StackPanel>(renderer.Root);
        Assert.IsType<Button>(panel.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var request = await transport.Entered.Task.WaitAsync(timeout.Token);
        Assert.IsType<TextBox>(panel.Children[0]).Text = "new query";
        while ((await session.SnapshotAsync(timeout.Token)).Version == 0)
        { await Task.Delay(10, timeout.Token); }
        transport.Reply.SetResult(new UiStatePatch(request.Snapshot.SessionId, request.Snapshot.Version,
            [new UiStateValue(text.Id, UiValue.FromString("old query"))]));
        while (session.LastInputError is null)
        { await Task.Delay(10, timeout.Token); }
        Assert.False(session.IsDisconnected);
        Assert.Equal("new query", (await session.SnapshotAsync()).State[0].Value.Text);
        Assert.Equal(1, renderer.Materializations);
    }

    [AvaloniaFact]
    public async Task Input_rate_limit_disconnects_and_releases_renderer()
    {
        var b = new UiBuilder();
        var text = b.State("");
        var renderer = new AvaloniaUiRenderer();
        using var sandbox = SandboxHost.Create(h => h.AddDefaultPureBindings());
        await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build(),
            new UiPolicy { MaxInputEventsPerSecond = 1 }).InstallAsync(b.Build(b.TextBox(text)), renderer);
        var box = Assert.IsType<TextBox>(renderer.Root);
        box.Text = "first";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while ((await session.SnapshotAsync(timeout.Token)).Version == 0)
        { await Task.Delay(1, timeout.Token); }
        box.Text = "second";
        while (!session.IsDisconnected)
        { await Task.Delay(1, timeout.Token); }
        await session.DisposeAsync();
        Assert.True(renderer.IsDisposed);
    }

    [AvaloniaFact]
    public async Task Repeated_install_uninstall_does_not_retain_roots_or_adapters()
    {
        using var sandbox = SandboxHost.Create(h => h.AddDefaultPureBindings());
        var host = new UiHost(sandbox, SandboxPolicyBuilder.Create().Build());
        var references = new List<WeakReference>();
        for (var i = 0; i < 10; i++)
        { references.AddRange(await InstallAndReleaseAsync(host)); }
        await Task.Delay(20, TestContext.Current.CancellationToken);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(references, r => Assert.False(r.IsAlive));
    }

    [AvaloniaFact]
    public async Task Oversized_text_edit_is_rejected_without_closing_session_and_valid_edit_recovers()
    {
        var b = new UiBuilder();
        var text = b.State("");
        var renderer = new AvaloniaUiRenderer();
        using var sandbox = SandboxHost.Create(h => h.AddDefaultPureBindings());
        await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build(),
            new UiPolicy { MaxStringLength = 8 }).InstallAsync(b.Build(b.TextBox(text)), renderer);
        var box = Assert.IsType<TextBox>(renderer.Root);
        box.Text = "too much text";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (session.LastInputError is null)
        { await Task.Delay(1, timeout.Token); }
        var rejected = await session.SnapshotAsync(timeout.Token);
        Assert.Equal(0, rejected.Version);
        Assert.Equal("", rejected.State[0].Value.Text);
        Assert.Equal("", box.Text);
        Assert.False(session.IsDisconnected);
        Assert.False(renderer.IsDisposed);
        box.Text = "valid";
        while ((await session.SnapshotAsync(timeout.Token)).Version == 0)
        { await Task.Delay(1, timeout.Token); }
        Assert.Equal("valid", (await session.SnapshotAsync(timeout.Token)).State[0].Value.Text);
        Assert.Equal(1, renderer.Materializations);
    }

    private static async Task<WeakReference[]> InstallAndReleaseAsync(UiHost host)
    {
        var b = new UiBuilder();
        var text = b.State("");
        var renderer = new AvaloniaUiRenderer();
        var session = await host.InstallAsync(b.Build(b.TextBox(text)), renderer);
        var references = new[] { new WeakReference(renderer), new WeakReference(renderer.Root!) };
        await session.DisposeAsync();
        return references;
    }

    private sealed class PendingTransport : IUiRemoteTransport
    {
        public TaskCompletionSource<UiRemoteEvent> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<UiStatePatch> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<UiStatePatch> DispatchAsync(UiRemoteEvent message, CancellationToken cancellationToken)
        {
            Entered.TrySetResult(message);
            return new ValueTask<UiStatePatch>(Reply.Task.WaitAsync(cancellationToken));
        }
    }
}

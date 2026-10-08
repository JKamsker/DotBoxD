using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.UI.Authoring;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Avalonia.Tests.Rendering;

public sealed class AvaloniaRendererTests
{
    [AvaloniaFact]
    public async Task Every_registered_primitive_materializes_and_captures_offscreen()
    {
        var builder = new UiBuilder();
        var text = builder.State("");
        var check = builder.State(false);
        var value = builder.State(20.0);
        var rows = builder.State(ImmutableArray.Create(new UiListItem("a", "Alpha")));
        var root = builder.Border(builder.Scroll(builder.Stack(
            builder.Grid(2, builder.Text("Title"), builder.Text("Value")),
            builder.TextBox(text), builder.CheckBox("Enabled", check), builder.Slider(value),
            builder.Progress(value), builder.Items(rows), builder.RemoteButton("Search", 7))), padding: 4);
        var renderer = new AvaloniaUiRenderer();
        using var sandbox = SandboxHost.Create(b => b.AddDefaultPureBindings());
        await using var session = await Host(sandbox).InstallAsync(builder.Build(root), renderer, new EmptyTransport());
        var window = new Window { Content = renderer.Root, Width = 400, Height = 300 };
        window.Show();
        using var frame = await renderer.CaptureAsync(new PixelSize(400, 300), new Vector(96, 96));
        Assert.Equal(new PixelSize(400, 300), frame.PixelSize);
        Assert.Equal(1, renderer.Materializations);
        Assert.IsType<Border>(renderer.Root);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Keyboard_focus_mouse_boolean_and_numeric_input_update_authoritative_state()
    {
        var b = new UiBuilder();
        var text = b.State("");
        var check = b.State(false);
        var number = b.State(0.0);
        var package = b.Build(b.Stack(b.TextBox(text), b.CheckBox("Toggle", check), b.Slider(number), b.RemoteButton("Go", 7)));
        var renderer = new AvaloniaUiRenderer();
        var transport = new EmptyTransport();
        using var sandbox = SandboxHost.Create(h => h.AddDefaultPureBindings());
        await using var session = await Host(sandbox).InstallAsync(package, renderer, transport);
        var window = new Window { Content = renderer.Root, Width = 400, Height = 300 };
        window.Show();
        var panel = Assert.IsType<StackPanel>(renderer.Root);
        var input = Assert.IsType<TextBox>(panel.Children[0]);
        Assert.True(input.Focus());
        window.KeyTextInput("search");
        await WaitAsync(session, s => s.State[0].Value.Text == "search");
        var checkbox = Assert.IsType<CheckBox>(panel.Children[1]);
        checkbox.IsChecked = true;
        var slider = Assert.IsType<Slider>(panel.Children[2]);
        slider.Value = 37;
        await WaitAsync(session, s => s.State[1].Value.Boolean && s.State[2].Value.Number == 37);
        window.UpdateLayout();
        var button = Assert.IsType<Button>(panel.Children[3]);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        await WaitAsync(session, _ => transport.Calls == 1);
        Assert.Same(panel, renderer.Root);
        Assert.Equal(1, renderer.Materializations);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Keyed_rows_preserve_identity_and_updates_from_background_threads_marshal_to_ui()
    {
        var b = new UiBuilder();
        var list = b.State(ImmutableArray.Create(new UiListItem("a", "Alpha"), new UiListItem("b", "Beta")));
        var renderer = new AvaloniaUiRenderer();
        using var sandbox = SandboxHost.Create(h => h.AddDefaultPureBindings());
        await using var session = await Host(sandbox).InstallAsync(b.Build(b.Items(list)), renderer);
        var panel = Assert.IsType<StackPanel>(renderer.Root);
        var alpha = panel.Children[0];
        var beta = panel.Children[1];
        var snapshot = await session.SnapshotAsync();
        await Task.Run(async () => await session.ApplyPatchAsync(new UiStatePatch(session.Id, snapshot.Version,
            [new UiStateValue(list.Id, UiValue.FromItems([new("b", "Updated"), new("a", "Alpha")]))])));
        Assert.Same(beta, panel.Children[0]);
        Assert.Same(alpha, panel.Children[1]);
        Assert.Equal("Updated", Assert.IsType<TextBlock>(beta).Text);
        await session.ApplyPatchAsync(new UiStatePatch(session.Id, 1,
            [new(list.Id, UiValue.FromItems([new("b", "Updated"), new("c", "Charlie")]))]));
        Assert.Same(beta, panel.Children[0]);
        Assert.DoesNotContain(alpha, panel.Children);
        Assert.Equal("Charlie", Assert.IsType<TextBlock>(panel.Children[1]).Text);
        Assert.Equal(2, renderer.Updates);
        Assert.Equal(1, renderer.Materializations);
    }

    [AvaloniaFact]
    public async Task Teardown_removes_subscriptions_controls_and_pending_reads()
    {
        var b = new UiBuilder();
        var state = b.State("");
        var renderer = new AvaloniaUiRenderer();
        using var sandbox = SandboxHost.Create(h => h.AddDefaultPureBindings());
        var session = await Host(sandbox).InstallAsync(b.Build(b.Stack(b.TextBox(state))), renderer);
        var panel = Assert.IsType<StackPanel>(renderer.Root);
        var box = Assert.IsType<TextBox>(panel.Children[0]);
        await session.DisposeAsync();
        box.Text = "detached";
        await session.DisposeAsync();
        Assert.True(renderer.IsDisposed);
        Assert.Null(renderer.Root);
        Assert.Empty(panel.Children);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SnapshotAsync().AsTask());
    }

    [AvaloniaFact]
    public async Task Input_queue_overflow_disconnects_without_unbounded_work()
    {
        var b = new UiBuilder();
        var text = b.State("");
        var renderer = new AvaloniaUiRenderer(inputCapacity: 1);
        using var sandbox = SandboxHost.Create(h => h.AddDefaultPureBindings());
        await using var session = await Host(sandbox).InstallAsync(b.Build(b.TextBox(text)), renderer);
        var box = Assert.IsType<TextBox>(renderer.Root);
        for (var i = 0; i < 100; i++)
        { box.Text = i.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!session.IsDisconnected)
        { await Task.Delay(10, timeout.Token); }
        await session.DisposeAsync();
        Assert.True(renderer.IsDisposed);
    }

    [AvaloniaFact]
    public async Task Sparse_and_state_bound_grid_rows_have_matching_definitions()
    {
        var b = new UiBuilder();
        var row = b.State(3);
        var first = b.Element(UiPrimitive.Text, [UiLiteral.Text("first").Property(UiPropertyId.Text),
            UiLiteral.Integer(0).Property(UiPropertyId.Row)]);
        var second = b.Element(UiPrimitive.Text, [UiLiteral.Text("second").Property(UiPropertyId.Text),
            ((UiBinding<int>)row).Property(UiPropertyId.Row)]);
        var renderer = new AvaloniaUiRenderer();
        using var sandbox = SandboxHost.Create(h => h.AddDefaultPureBindings());
        await using var session = await Host(sandbox).InstallAsync(b.Build(b.Grid(1, first, second)), renderer);
        var grid = Assert.IsType<Grid>(renderer.Root);
        Assert.Equal(4, grid.RowDefinitions.Count);
        Assert.Equal(3, Grid.GetRow(grid.Children[1]));
        await session.ApplyPatchAsync(new UiStatePatch(session.Id, 0, [new(row.Id, UiValue.FromInt32(5))]));
        Assert.Equal(6, grid.RowDefinitions.Count);
        await session.ApplyPatchAsync(new UiStatePatch(session.Id, 1, [new(row.Id, UiValue.FromInt32(1))]));
        Assert.Equal(2, grid.RowDefinitions.Count);
        Assert.Same(grid, renderer.Root);
    }

    private static UiHost Host(SandboxHost sandbox) => new(sandbox, SandboxPolicyBuilder.Create().Build());
    private static async Task WaitAsync(UiSession session, Func<UiSnapshot, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition(await session.SnapshotAsync(timeout.Token)))
        { await Task.Delay(10, timeout.Token); }
    }
    private sealed class EmptyTransport : IUiRemoteTransport
    {
        public int Calls { get; private set; }
        public ValueTask<UiStatePatch> DispatchAsync(UiRemoteEvent message, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(new UiStatePatch(message.Snapshot.SessionId, message.Snapshot.Version, []));
        }
    }
}

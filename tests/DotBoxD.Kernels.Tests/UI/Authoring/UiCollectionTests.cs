using DotBoxD.Kernels.Policies;
using DotBoxD.UI;
using DotBoxD.UI.Authoring;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiCollectionTests
{
    [Fact]
    public async Task Item_materialization_budget_is_checked_before_initial_controls_and_atomic_patches()
    {
        var b = new UiBuilder();
        var items = b.State(System.Collections.Immutable.ImmutableArray.Create(new UiListItem("a", "Alpha")));
        var package = b.Build(b.Items(items));
        var renderer = new RecordingUiRenderer();
        using var sandbox = UiTestFixture.Sandbox();
        await using var session = await UiTestFixture.Host(sandbox, new UiPolicy { MaxNodes = 3 }).InstallAsync(package, renderer);
        await Assert.ThrowsAsync<UiValidationException>(() => session.ApplyPatchAsync(new UiStatePatch(session.Id, 0,
            [new(items.Id, UiValue.FromItems([new("a", "A"), new("b", "B"), new("c", "C")]))])).AsTask());
        Assert.Equal(0, (await session.SnapshotAsync()).Version);
        Assert.Empty(renderer.Updates);
        var tooMany = package with { State = [new(items.Id, UiValue.FromItems([new("a", "A"), new("b", "B"), new("c", "C")]))] };
        var rejected = new RecordingUiRenderer();
        await Assert.ThrowsAsync<UiValidationException>(() => UiTestFixture.Host(sandbox, new UiPolicy { MaxNodes = 3 })
            .InstallAsync(tooMany, rejected).AsTask());
        Assert.Equal(0, rejected.Materializations);
        Assert.Equal(1, rejected.Disposals);
    }

    [Fact]
    public void Duplicate_keys_and_unknown_list_members_fail_closed()
    {
        var b = new UiBuilder();
        var items = b.State(System.Collections.Immutable.ImmutableArray.Create(new UiListItem("a", "A")));
        var package = b.Build(b.Items(items));
        var invalid = package with { State = [new(items.Id, UiValue.FromItems([new("a", "A"), new("a", "B")]))] };
        Assert.Throws<UiValidationException>(() => UiPackageValidator.Validate(invalid, new UiPolicy()));
        var json = UiPackageJson.Export(package, new UiPolicy()).Replace("\"key\":\"a\"", "\"key\":\"a\",\"controlType\":\"Malicious.Control\"", StringComparison.Ordinal);
        Assert.Throws<UiValidationException>(() => UiPackageJson.Import(json, new UiPolicy()));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Both_kernel_modes_use_ordinary_validation_and_metering(ExecutionMode mode)
    {
        using var sandbox = UiTestFixture.Sandbox();
        var host = new UiHost(sandbox, SandboxPolicyBuilder.Create().WithFuel(10_000).Build(),
            execution: new SandboxExecutionOptions { Mode = mode });
        await using var session = await host.InstallAsync(UiTestFixture.Counter() with { Events = [UiTestFixture.Counter().Events[0]], RemoteEndpoints = [] }, new RecordingUiRenderer());
        Assert.Equal(1, UiTestFixture.Slot(await session.DispatchAsync(1), 1).Integer);
        var package = UiTestFixture.Counter() with { Events = [UiTestFixture.Counter().Events[0]], RemoteEndpoints = [] };
        var invalid = package with
        {
            Kernels = package.Kernels.SetItem(0, package.Kernels[0] with
            { ModuleJson = package.Kernels[0].ModuleJson.Replace("\"i32\":1", "\"string\":\"bad\"", StringComparison.Ordinal) })
        };
        var renderer = new RecordingUiRenderer();
        await Assert.ThrowsAnyAsync<Exception>(() => host.InstallAsync(invalid, renderer).AsTask());
        Assert.Equal(0, renderer.Materializations);
    }

    [Fact]
    public void Reusable_components_compose_without_host_implementation_types()
    {
        var b = new UiBuilder();
        var package = b.Build(b.Stack(new LabelComponent("one").Render(b), new LabelComponent("two").Render(b)));
        Assert.Equal(3, package.Nodes.Length);
        Assert.Equal(UiPackageJson.ComputeHash(package, new UiPolicy()),
            UiPackageJson.ComputeHash(UiPackageJson.Import(UiPackageJson.Export(package, new UiPolicy()), new UiPolicy()), new UiPolicy()));
    }

    [Fact]
    public async Task Numeric_input_outside_control_range_is_rejected_atomically()
    {
        var b = new UiBuilder();
        var value = b.State(50.0);
        var slider = b.Slider(value, maximum: 100);
        var renderer = new RecordingUiRenderer();
        using var sandbox = UiTestFixture.Sandbox();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(b.Build(slider), renderer);
        await Assert.ThrowsAsync<UiValidationException>(() => session.SetInputAsync(slider.Id, UiPropertyId.Value,
            UiValue.FromNumber(200)).AsTask());
        Assert.Equal(0, (await session.SnapshotAsync()).Version);
        Assert.Empty(renderer.Updates);
    }

    [Fact]
    public async Task Equal_decoded_values_do_not_resend_unchanged_properties()
    {
        var b = new UiBuilder();
        var text = b.State("");
        var input = b.TextBox(text);
        var renderer = new RecordingUiRenderer();
        using var sandbox = UiTestFixture.Sandbox();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(b.Build(input), renderer);
        var snapshot = await session.SetInputAsync(input.Id, UiPropertyId.Text, UiValue.FromString(""));
        Assert.Equal(1, snapshot.Version);
        Assert.Empty(renderer.Updates);
        Assert.Equal(UiValue.FromString(""), snapshot.State[0].Value);
        Assert.Equal(UiValue.FromString("").GetHashCode(), snapshot.State[0].Value.GetHashCode());
    }

    [Fact]
    public void Foreign_elements_are_rejected_before_local_nodes_are_mutated()
    {
        var builder = new UiBuilder();
        var local = builder.Text("local");
        var foreign = new UiBuilder().Text("foreign");
        Assert.Equal(local.Id, foreign.Id);
        Assert.Throws<ArgumentException>(() => builder.Grid(1, local, foreign));
        Assert.Throws<ArgumentException>(() => builder.Stack(local, foreign));
        Assert.Throws<ArgumentException>(() => builder.Build(foreign));
        Assert.Throws<ArgumentException>(() => builder.Stack([null!]));
        var node = Assert.Single(builder.Build(local).Nodes);
        Assert.Equal(UiPropertyId.Text, Assert.Single(node.Properties).Id);
    }

    [Fact]
    public async Task Keyed_rows_obey_the_independent_child_limit_before_install()
    {
        var b = new UiBuilder();
        var rows = b.State(System.Collections.Immutable.ImmutableArray.Create(new UiListItem("a", "A"), new UiListItem("b", "B")));
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new RecordingUiRenderer();
        await Assert.ThrowsAsync<UiValidationException>(() => UiTestFixture.Host(sandbox,
            new UiPolicy { MaxChildren = 1, MaxItems = 10, MaxNodes = 10 }).InstallAsync(b.Build(b.Items(rows)), renderer).AsTask());
        Assert.Equal(0, renderer.Materializations);
        Assert.Equal(1, renderer.Disposals);
    }

    [Fact]
    public async Task Keyed_rows_obey_the_independent_child_limit_before_patch_commit()
    {
        var b = new UiBuilder();
        var rows = b.State(System.Collections.Immutable.ImmutableArray.Create(new UiListItem("a", "A")));
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new RecordingUiRenderer();
        await using var session = await UiTestFixture.Host(sandbox,
            new UiPolicy { MaxChildren = 1, MaxItems = 10, MaxNodes = 10 }).InstallAsync(b.Build(b.Items(rows)), renderer);
        await Assert.ThrowsAsync<UiValidationException>(() => session.ApplyPatchAsync(new UiStatePatch(session.Id, 0,
            [new(rows.Id, UiValue.FromItems([new("a", "A"), new("b", "B")]))])).AsTask());
        Assert.Equal(0, (await session.SnapshotAsync()).Version);
        Assert.Empty(renderer.Updates);
    }

    private sealed class LabelComponent(string text) : IUiComponent
    {
        public UiElement Render(UiBuilder builder) => builder.Text(text);
    }
}

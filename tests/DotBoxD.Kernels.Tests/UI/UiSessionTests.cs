using DotBoxD.UI;

namespace DotBoxD.Kernels.Tests.UI;

public sealed class UiSessionTests
{
    [Fact]
    public async Task Local_kernel_and_two_way_inputs_update_without_remote_calls_or_tree_rebuilds()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new RecordingUiRenderer();
        var remote = TestUiTransport.Echo();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, remote);

        var clicked = await session.DispatchAsync(1);
        Assert.Equal(1, UiTestFixture.Slot(clicked, 1).Integer);
        Assert.Equal(1, clicked.Version);
        Assert.Equal("1", Assert.Single(renderer.Updates[0]).Value.Text);
        var typed = await session.SetInputAsync(4, UiPropertyId.Text, UiValue.FromString("abc"));
        Assert.Equal("abc", UiTestFixture.Slot(typed, 2).Text);
        var toggled = await session.SetInputAsync(6, UiPropertyId.Checked, UiValue.FromBoolean(true));
        Assert.True(UiTestFixture.Slot(toggled, 4).Boolean);
        Assert.Equal(3, toggled.Version);
        Assert.Equal(0, remote.Calls);
        Assert.Equal(1, renderer.Materializations);
    }

    [Fact]
    public async Task Remote_event_carries_consistent_snapshot_and_applies_one_batched_patch()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var remote = new TestUiTransport((request, _) =>
        {
            Assert.Equal(7, request.EndpointId);
            Assert.Equal("query", UiTestFixture.Slot(request.Snapshot, 2).Text);
            return ValueTask.FromResult(UiTestFixture.Reply(request,
                new UiStateValue(2, UiValue.FromString("done")), new UiStateValue(3, UiValue.FromString("results"))));
        });
        var renderer = new RecordingUiRenderer();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, remote);
        await session.SetInputAsync(4, UiPropertyId.Text, UiValue.FromString("query"));
        var result = await session.DispatchAsync(2);
        Assert.Equal(2, result.Version);
        Assert.Equal("done", UiTestFixture.Slot(result, 2).Text);
        Assert.Equal("results", UiTestFixture.Slot(result, 3).Text);
        Assert.Equal(1, remote.Calls);
        Assert.Equal(2, renderer.Updates.Count);
    }

    [Fact]
    public async Task Invalid_and_cross_session_patches_are_atomic()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var renderer = new RecordingUiRenderer();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), renderer, TestUiTransport.Echo());
        var writes = new UiStateValue[] { new(2, UiValue.FromString("changed")), new(999, UiValue.FromInt32(1)) };
        await Assert.ThrowsAsync<UiValidationException>(async () =>
            await session.ApplyPatchAsync(new UiStatePatch(session.Id, 0, [.. writes])));
        await Assert.ThrowsAsync<UiValidationException>(async () =>
            await session.ApplyPatchAsync(new UiStatePatch(Guid.NewGuid(), 0, [new(2, UiValue.FromString("forged"))])));
        await Assert.ThrowsAsync<UiValidationException>(async () =>
            await session.ApplyPatchAsync(new UiStatePatch(session.Id, 0, [new(2, UiValue.FromString("a")), new(2, UiValue.FromString("b"))])));
        await Assert.ThrowsAsync<UiValidationException>(async () =>
            await session.ApplyPatchAsync(new UiStatePatch(session.Id, 0, [new(1, UiValue.FromString("wrong type"))])));
        Assert.Equal(0, (await session.SnapshotAsync()).Version);
        Assert.Empty(UiTestFixture.Slot(await session.SnapshotAsync(), 2).Text);
        Assert.Empty(renderer.Updates);
    }

    [Fact]
    public async Task Undeclared_input_and_event_routes_fail_closed()
    {
        using var sandbox = UiTestFixture.Sandbox();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(UiTestFixture.Counter(), new RecordingUiRenderer(), TestUiTransport.Echo());
        await Assert.ThrowsAsync<UiValidationException>(async () => await session.DispatchAsync(99));
        await Assert.ThrowsAsync<UiValidationException>(async () => await session.SetInputAsync(3, UiPropertyId.Text, UiValue.FromString("bad")));
        Assert.Equal(0, (await session.SnapshotAsync()).Version);
    }

    [Fact]
    public async Task Kernel_fuel_exhaustion_preserves_state_and_rendered_values()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var package = UiTestFixture.Counter();
        var loop = package.Kernels[0].ModuleJson.Replace("\"body\":[", "\"body\":[{\"op\":\"while\",\"condition\":{\"bool\":true},\"body\":[]},", StringComparison.Ordinal);
        package = package with { Kernels = [package.Kernels[0] with { ModuleJson = loop }, package.Kernels[1]] };
        var renderer = new RecordingUiRenderer();
        await using var session = await UiTestFixture.Host(sandbox, fuel: 100).InstallAsync(package, renderer, TestUiTransport.Echo());
        await Assert.ThrowsAsync<UiValidationException>(async () => await session.DispatchAsync(1));
        var snapshot = await session.SnapshotAsync();
        Assert.Equal(0, snapshot.Version);
        Assert.Equal(0, UiTestFixture.Slot(snapshot, 1).Integer);
        Assert.Empty(renderer.Updates);
    }

    [Fact]
    public async Task Binding_failure_rolls_back_the_entire_candidate_patch()
    {
        using var sandbox = UiTestFixture.Sandbox();
        var package = UiTestFixture.Counter();
        var divide = UiTestFixture.Kernel("I32", "String",
            """{"call":"int32.toStringInvariant","args":[{"op":"div","left":{"i32":10},"right":{"var":"value"}}]}""");
        package = package with
        {
            State = [package.State[0] with { InitialValue = UiValue.FromInt32(1) }, .. package.State.Skip(1)],
            Kernels = [package.Kernels[0], package.Kernels[1] with { ModuleJson = divide }]
        };
        var renderer = new RecordingUiRenderer();
        await using var session = await UiTestFixture.Host(sandbox).InstallAsync(package, renderer, TestUiTransport.Echo());
        await Assert.ThrowsAsync<UiValidationException>(async () => await session.ApplyPatchAsync(
            new UiStatePatch(session.Id, 0, [new(1, UiValue.FromInt32(0)), new(2, UiValue.FromString("changed"))])));
        Assert.Equal(1, UiTestFixture.Slot(await session.SnapshotAsync(), 1).Integer);
        Assert.Empty(UiTestFixture.Slot(await session.SnapshotAsync(), 2).Text);
        Assert.Empty(renderer.Updates);
    }
}

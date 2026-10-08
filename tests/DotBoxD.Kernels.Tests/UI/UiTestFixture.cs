using System.Collections.Immutable;
using DotBoxD.Kernels.Policies;
using DotBoxD.UI;
using DotBoxD.UI.Runtime;

namespace DotBoxD.Kernels.Tests.UI;

internal static class UiTestFixture
{
    public static SandboxHost Sandbox() => SandboxHost.Create(b => b.AddDefaultPureBindings());
    public static SandboxHost CompiledSandbox()
        => SandboxHost.Create(b => b.AddDefaultPureBindings().UseCompilerIfAvailable());
    public static UiHost Host(SandboxHost sandbox, UiPolicy? policy = null, long fuel = 10_000)
        => new(sandbox, SandboxPolicyBuilder.Create().WithFuel(fuel).Build(), policy);

    public static UiPackage Counter() => new(1, 1,
        [new UiNode(1, UiPrimitive.Stack, [2, 3, 4, 5, 6], []),
         new UiNode(2, UiPrimitive.Text, [], [new UiProperty(UiPropertyId.Text, BindingKernelId: 2)]),
         new UiNode(3, UiPrimitive.Button, [], [new UiProperty(UiPropertyId.Text, UiValue.FromString("Increment"))]),
         new UiNode(4, UiPrimitive.TextBox, [], [new UiProperty(UiPropertyId.Text, StateSlotId: 2, TwoWay: true)]),
         new UiNode(5, UiPrimitive.Button, [], [new UiProperty(UiPropertyId.Text, UiValue.FromString("Search"))]),
         new UiNode(6, UiPrimitive.CheckBox, [], [new UiProperty(UiPropertyId.Checked, StateSlotId: 4, TwoWay: true)])],
        [new UiStateSlot(1, UiValue.FromInt32(0)), new UiStateSlot(2, UiValue.FromString("")),
         new UiStateSlot(3, UiValue.FromString("")), new UiStateSlot(4, UiValue.FromBoolean(false))],
        [new UiKernel(1, Kernel("I32", "I32", """{"op":"add","left":{"var":"value"},"right":{"i32":1}}"""), "main", 1),
         new UiKernel(2, Kernel("I32", "String", """{"call":"int32.toStringInvariant","args":[{"var":"value"}]}"""), "main", 1)],
        [new UiEvent(1, 3, UiEventKind.Click, UiEventTarget.LocalKernel, KernelId: 1, OutputSlotId: 1),
         new UiEvent(2, 5, UiEventKind.Click, UiEventTarget.Remote, RemoteEndpointId: 7)], [7]);

    public static string Kernel(string input, string output, string expression)
        => $$"""
        {"id":"ui-test","version":"1.0.0","targetSandboxVersion":"1.0.0",
        "capabilityRequests":[],"functions":[{"id":"main","visibility":"entrypoint",
        "parameters":[{"name":"value","type":"{{input}}"}],"returnType":"{{output}}",
        "body":[{"op":"return","value":{{expression}}}]}]}
        """;

    public static UiValue Slot(UiSnapshot snapshot, int id) => snapshot.State.Single(s => s.SlotId == id).Value;

    public static UiStatePatch Reply(UiRemoteEvent request, params UiStateValue[] writes)
        => new(request.Snapshot.SessionId, request.Snapshot.Version, [.. writes]);
}

internal sealed class RecordingUiRenderer : IUiRenderer
{
    public int Materializations { get; private set; }
    public int Disposals { get; private set; }
    public bool ThrowOnMaterialize { get; init; }
    public bool ThrowOnUpdate { get; init; }
    public List<ImmutableArray<UiPropertyValue>> Updates { get; } = [];
    public ImmutableArray<UiPropertyValue> Initial { get; private set; }

    public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
    {
        Materializations++;
        Initial = values;
        if (ThrowOnMaterialize)
        { throw new InvalidOperationException("renderer materialization"); }
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
    {
        if (ThrowOnUpdate)
        { throw new InvalidOperationException("renderer update"); }
        Updates.Add(changes);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposals++;
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestUiTransport(Func<UiRemoteEvent, CancellationToken, ValueTask<UiStatePatch>> dispatch) : IUiRemoteTransport
{
    public int Calls { get; private set; }

    public ValueTask<UiStatePatch> DispatchAsync(UiRemoteEvent message, CancellationToken cancellationToken)
    {
        Calls++;
        return dispatch(message, cancellationToken);
    }

    public static TestUiTransport Echo() => new((request, _) => ValueTask.FromResult(UiTestFixture.Reply(request)));
}

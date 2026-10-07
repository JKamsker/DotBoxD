using System.Collections.Immutable;
using DotBoxD.UI;

namespace Examples.SandboxedUi.Plugin;

/// <summary>Hand-written public primitives; reusable C# composition needs no host-side plugin class.</summary>
internal static class CounterComponent
{
    public static UiPackage Package() => new(1, 1,
        [new UiNode(1, UiPrimitive.Stack, [2, 3, 4, 5, 6], []), .. Counter(), .. Search()],
        [new UiStateSlot(1, UiValue.FromInt32(0)), new UiStateSlot(2, UiValue.FromString("")),
         new UiStateSlot(3, UiValue.FromString(""))],
        [new UiKernel(1, Module("I32", """{"op":"add","left":{"var":"count"},"right":{"i32":1}}"""), "main", 1),
         new UiKernel(2, Module("String", """{"call":"int32.toStringInvariant","args":[{"var":"count"}]}"""), "main", 1)],
        [new UiEvent(1, 3, UiEventKind.Click, UiEventTarget.LocalKernel, KernelId: 1, OutputSlotId: 1),
         new UiEvent(2, 5, UiEventKind.Click, UiEventTarget.Remote, RemoteEndpointId: 7)], [7]);

    private static ImmutableArray<UiNode> Counter() =>
        [new UiNode(2, UiPrimitive.Text, [], [new UiProperty(UiPropertyId.Text, BindingKernelId: 2)]),
         new UiNode(3, UiPrimitive.Button, [], [new UiProperty(UiPropertyId.Text, UiValue.FromString("Increment"))])];

    private static ImmutableArray<UiNode> Search() =>
        [new UiNode(4, UiPrimitive.TextBox, [], [new UiProperty(UiPropertyId.Text, StateSlotId: 2, TwoWay: true)]),
         new UiNode(5, UiPrimitive.Button, [], [new UiProperty(UiPropertyId.Text, UiValue.FromString("Search"))]),
         new UiNode(6, UiPrimitive.Text, [], [new UiProperty(UiPropertyId.Text, StateSlotId: 3)])];

    private static string Module(string output, string expression) => $$"""
        {"id":"counter","version":"1.0.0","targetSandboxVersion":"1.0.0","capabilityRequests":[],
        "functions":[{"id":"main","visibility":"entrypoint","parameters":[{"name":"count","type":"I32"}],
        "returnType":"{{output}}","body":[{"op":"return","value":{{expression}}}]}]}
        """;
}

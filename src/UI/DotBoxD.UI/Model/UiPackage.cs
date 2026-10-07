using System.Collections.Immutable;

namespace DotBoxD.UI;

/// <summary>Data and restricted IR only; no renderer objects or plugin implementation types.</summary>
public sealed record UiPackage(
    int FormatVersion,
    int RootNodeId,
    ImmutableArray<UiNode> Nodes,
    ImmutableArray<UiStateSlot> State,
    ImmutableArray<UiKernel> Kernels,
    ImmutableArray<UiEvent> Events,
    ImmutableArray<int> RemoteEndpoints)
{
    public const int CurrentFormatVersion = 1;
}

public enum UiPrimitive
{
    Stack = 1,
    Grid = 2,
    Border = 3,
    Text = 4,
    Button = 5,
    TextBox = 6,
    CheckBox = 7,
    ProgressBar = 8,
    ScrollViewer = 9,
    Items = 10
}

public enum UiPropertyId
{
    Text = 1,
    Enabled = 2,
    Visible = 3,
    Value = 4,
    Maximum = 5,
    Checked = 6
}

public sealed record UiNode(
    int Id,
    UiPrimitive Primitive,
    ImmutableArray<int> Children,
    ImmutableArray<UiProperty> Properties);

/// <summary>Exactly one of Literal, StateSlotId or BindingKernelId supplies the value.</summary>
public sealed record UiProperty(
    UiPropertyId Id,
    UiValue? Literal = null,
    int StateSlotId = 0,
    int BindingKernelId = 0,
    bool TwoWay = false);

public sealed record UiStateSlot(int Id, UiValue InitialValue);

/// <summary>A scalar-to-scalar (or Unit-to-scalar) entrypoint in ordinary DotBoxD restricted IR.</summary>
public sealed record UiKernel(int Id, string ModuleJson, string Entrypoint, int InputSlotId = 0);

public enum UiEventKind
{
    Click = 1
}

public enum UiEventTarget
{
    LocalKernel = 1,
    Remote = 2
}

/// <summary>Local events return a value for OutputSlotId; remote events address an endpoint ID.</summary>
public sealed record UiEvent(
    int Id,
    int NodeId,
    UiEventKind Kind,
    UiEventTarget Target,
    int KernelId = 0,
    int OutputSlotId = 0,
    int RemoteEndpointId = 0);

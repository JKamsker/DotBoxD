using System.Collections.Immutable;

namespace DotBoxD.UI.Blazor;

/// <summary>Trusted presentation data; Blazor render trees are never part of the worker protocol.</summary>
public sealed record BlazorUiSnapshot(
    UiPackage Package,
    ImmutableDictionary<int, UiNode> Nodes,
    ImmutableDictionary<(int Node, UiPropertyId Property), UiValue> Values)
{
    public UiValue? Get(int nodeId, UiPropertyId property) => Values.GetValueOrDefault((nodeId, property));
}

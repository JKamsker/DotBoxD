using System.Collections.Immutable;

namespace DotBoxD.UI.Blazor;

/// <summary>Trusted presentation data; Blazor render trees are never part of the worker protocol.</summary>
public sealed record BlazorUiSnapshot(
    UiPackage Package,
    ImmutableDictionary<int, UiNode> Nodes,
    ImmutableDictionary<(int Node, UiPropertyId Property), UiValue> Values)
{
    public ImmutableDictionary<int, Runtime.UiResolvedExtension<IUiBlazorExtension>> Extensions { get; init; } = ImmutableDictionary<int, Runtime.UiResolvedExtension<IUiBlazorExtension>>.Empty;
    public ImmutableDictionary<int, BlazorUiImage> Images { get; init; } = ImmutableDictionary<int, BlazorUiImage>.Empty;
    public UiValue? Get(int nodeId, UiPropertyId property) => Values.GetValueOrDefault((nodeId, property));
}

/// <summary>Trusted bounded image projection; Source is encoded by the adapter from host pixels.</summary>
public sealed record BlazorUiImage(int Width, int Height, string Source);

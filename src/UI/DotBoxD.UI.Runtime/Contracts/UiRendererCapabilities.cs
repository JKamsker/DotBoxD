using System.Collections.Immutable;

namespace DotBoxD.UI.Runtime;

/// <summary>Trusted adapter support, checked before any package is materialized.</summary>
public sealed record UiRendererCapabilities(
    ImmutableHashSet<UiPrimitive> Primitives,
    ImmutableHashSet<UiFeature> Features)
{
    public ImmutableHashSet<string> ExtensionSchemas { get; init; } = ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    public static UiRendererCapabilities Core { get; } = new(
        [.. Enum.GetValues<UiPrimitive>()], [.. Enum.GetValues<UiFeature>()]);

    public void Validate(UiPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (Primitives is null || Features is null || ExtensionSchemas is null ||
            package.Extensions.Any(e => !ExtensionSchemas.Contains(e.SchemaId)) ||
            package.Nodes.Any(n => !Primitives.Contains(n.Primitive)) ||
            package.RequiredFeatures.Any(f => !Features.Contains(f)))
        { throw new UiValidationException("UI package requires unsupported renderer primitives or features."); }
    }
}

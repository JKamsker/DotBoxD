using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ImmutableHashSetBuilderCapacityPolicy
{
    private const string BuilderTypeName = "System.Collections.Immutable.ImmutableHashSet<T>.Builder";

    public const string UnionWithDisplayName = BuilderTypeName + ".UnionWith";

    public static bool IsUnionWith(IMethodSymbol method, string typeName)
        => method is { MethodKind: MethodKind.Ordinary, Name: "UnionWith" } &&
           string.Equals(typeName, BuilderTypeName, StringComparison.Ordinal);
}

using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ImmutableSortedSetBuilderCapacityPolicy
{
    private const string BuilderTypeName =
        "System.Collections.Immutable.ImmutableSortedSet<T>.Builder";

    public const string UnionWithDisplayName = BuilderTypeName + ".UnionWith";

    public static bool IsUnboundedUnionWith(IMethodSymbol method, string typeName)
        => method is { IsStatic: false, Name: "UnionWith" } &&
           typeName == BuilderTypeName;
}

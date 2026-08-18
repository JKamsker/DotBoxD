using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ImmutableSortedDictionaryBuilderPolicy
{
    private const string BuilderTypeName =
        "System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>.Builder";

    public const string AddRangeDisplayName = BuilderTypeName + ".AddRange";

    public static bool IsAddRange(IMethodSymbol method, string typeName)
        => method is { MethodKind: MethodKind.Ordinary, Name: "AddRange" } &&
           string.Equals(typeName, BuilderTypeName, StringComparison.Ordinal);
}

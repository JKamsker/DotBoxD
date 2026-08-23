using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class CollectionMaterializationPolicy
{
    private const string EnumerableTypeName = "System.Linq.Enumerable";
    private const string FrozenSetTypeName = "System.Collections.Frozen.FrozenSet";
    private const string ImmutableDictionaryTypeName = "System.Collections.Immutable.ImmutableDictionary";
    private const string ImmutableListTypeName = "System.Collections.Immutable.ImmutableList";
    private const string ListTypeName = "System.Collections.Generic.List<T>";

    public static bool TryGetDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        if (IsEnumerableMaterialization(method, typeName))
        {
            forbidden = $"System.Linq.Enumerable.{method.Name}";
            return true;
        }

        forbidden = (method.Name, typeName) switch
        {
            ("ToFrozenSet", FrozenSetTypeName) when method.IsStatic =>
                "System.Collections.Frozen.FrozenSet.ToFrozenSet",
            ("ToImmutableList", ImmutableListTypeName) when method.IsStatic =>
                "System.Collections.Immutable.ImmutableList.ToImmutableList",
            ("ToImmutableDictionary", ImmutableDictionaryTypeName) when method.IsStatic =>
                "System.Collections.Immutable.ImmutableDictionary.ToImmutableDictionary",
            ("GetRange", ListTypeName) when !method.IsStatic =>
                "System.Collections.Generic.List.GetRange",
            _ => null!
        };
        return forbidden is not null;
    }

    private static bool IsEnumerableMaterialization(IMethodSymbol method, string typeName)
        => method is { IsStatic: true } &&
           method.Name is "ToArray" or "ToList" or "ToLookup" &&
           string.Equals(typeName, EnumerableTypeName, StringComparison.Ordinal);
}

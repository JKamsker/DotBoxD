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
        if (TryGetEnumerableDisplayName(method, typeName, out forbidden))
        {
            return true;
        }

        return TryGetCollectionDisplayName(method, typeName, out forbidden);
    }

    private static bool TryGetEnumerableDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        if (IsEnumerableMaterialization(method, typeName))
        {
            forbidden = $"System.Linq.Enumerable.{method.Name}";
            return true;
        }

        forbidden = null!;
        return false;
    }

    private static bool TryGetCollectionDisplayName(IMethodSymbol method, string typeName, out string forbidden)
        => TryGetStaticCollectionDisplayName(method, typeName, out forbidden) ||
           TryGetListDisplayName(method, typeName, out forbidden);

    private static bool TryGetStaticCollectionDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        forbidden = (method.Name, typeName) switch
        {
            ("ToFrozenSet", FrozenSetTypeName) when method.IsStatic =>
                "System.Collections.Frozen.FrozenSet.ToFrozenSet",
            ("ToImmutableList", ImmutableListTypeName) when method.IsStatic =>
                "System.Collections.Immutable.ImmutableList.ToImmutableList",
            ("ToImmutableDictionary", ImmutableDictionaryTypeName) when method.IsStatic =>
                "System.Collections.Immutable.ImmutableDictionary.ToImmutableDictionary",
            _ => null!
        };

        return forbidden is not null;
    }

    private static bool TryGetListDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        forbidden = (method.Name, typeName) switch
        {
            ("ConvertAll", ListTypeName) when !method.IsStatic =>
                "System.Collections.Generic.List.ConvertAll",
            ("GetRange", ListTypeName) when !method.IsStatic =>
                "System.Collections.Generic.List.GetRange",
            ("FindIndex", ListTypeName) when !method.IsStatic =>
                "System.Collections.Generic.List.FindIndex",
            ("RemoveRange", ListTypeName) when !method.IsStatic =>
                "System.Collections.Generic.List.RemoveRange",
            ("Reverse", ListTypeName) when !method.IsStatic =>
                "System.Collections.Generic.List.Reverse",
            _ => null!
        };

        return forbidden is not null;
    }

    private static bool IsEnumerableMaterialization(IMethodSymbol method, string typeName)
        => method is { IsStatic: true } &&
           method.Name is "ToArray" or "ToList" or "ToLookup" &&
           string.Equals(typeName, EnumerableTypeName, StringComparison.Ordinal);
}

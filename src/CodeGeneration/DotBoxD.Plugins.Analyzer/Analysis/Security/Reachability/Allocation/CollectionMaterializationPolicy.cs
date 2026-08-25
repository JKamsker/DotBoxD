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

        forbidden = GetCollectionDisplayName(method, typeName);
        return forbidden is not null;
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

    private static string GetCollectionDisplayName(IMethodSymbol method, string typeName)
    {
        if (TryGetStaticCollectionDisplayName(method, typeName, out var forbidden))
        {
            return forbidden;
        }

        return GetListDisplayName(method, typeName);
    }

    private static bool TryGetStaticCollectionDisplayName(
        IMethodSymbol method,
        string typeName,
        out string forbidden)
    {
        forbidden = method.IsStatic
            ? (method.Name, typeName) switch
            {
                ("ToFrozenSet", FrozenSetTypeName) => "System.Collections.Frozen.FrozenSet.ToFrozenSet",
                ("ToImmutableList", ImmutableListTypeName) => "System.Collections.Immutable.ImmutableList.ToImmutableList",
                ("ToImmutableDictionary", ImmutableDictionaryTypeName) =>
                    "System.Collections.Immutable.ImmutableDictionary.ToImmutableDictionary",
                _ => null!
            }
            : null!;

        return forbidden is not null;
    }

    private static string GetListDisplayName(IMethodSymbol method, string typeName)
        => !method.IsStatic && string.Equals(typeName, ListTypeName, StringComparison.Ordinal)
            ? method.Name switch
            {
                "GetRange" => "System.Collections.Generic.List.GetRange",
                "RemoveRange" => "System.Collections.Generic.List.RemoveRange",
                _ => null!
            }
            : null!;

    private static bool IsEnumerableMaterialization(IMethodSymbol method, string typeName)
        => method is { IsStatic: true } &&
           method.Name is "ToArray" or "ToList" or "ToLookup" &&
           string.Equals(typeName, EnumerableTypeName, StringComparison.Ordinal);
}

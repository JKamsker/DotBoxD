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
        => !method.IsStatic && string.Equals(typeName, ListTypeName, StringComparison.Ordinal)
            ? GetListDisplayName(method.Name)
            : GetStaticCollectionDisplayName(method, typeName);

    private static string GetListDisplayName(string methodName)
        => methodName switch
        {
            "ConvertAll" => "System.Collections.Generic.List.ConvertAll",
            "GetRange" => "System.Collections.Generic.List.GetRange",
            _ => null!
        };

    private static string GetStaticCollectionDisplayName(IMethodSymbol method, string typeName)
        => (method.Name, typeName) switch
        {
            ("ToFrozenSet", FrozenSetTypeName) when method.IsStatic =>
                "System.Collections.Frozen.FrozenSet.ToFrozenSet",
            ("ToImmutableList", ImmutableListTypeName) when method.IsStatic =>
                "System.Collections.Immutable.ImmutableList.ToImmutableList",
            ("ToImmutableDictionary", ImmutableDictionaryTypeName) when method.IsStatic =>
                "System.Collections.Immutable.ImmutableDictionary.ToImmutableDictionary",
            _ => null!
        };

    private static bool IsEnumerableMaterialization(IMethodSymbol method, string typeName)
        => method is { IsStatic: true } &&
           method.Name is "ToArray" or "ToList" or "ToLookup" &&
           string.Equals(typeName, EnumerableTypeName, StringComparison.Ordinal);
}

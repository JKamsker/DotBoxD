using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class CollectionBulkGrowthPolicy
{
    private const string DictionaryInterfaceTypeName =
        "System.Collections.Generic.IDictionary<TKey, TValue>";
    private const string NonGenericDictionaryInterfaceTypeName = "System.Collections.IDictionary";
    private const string NonGenericSortedListTypeName = "System.Collections.SortedList";
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string PriorityQueueTypeName =
        "System.Collections.Generic.PriorityQueue<TElement, TPriority>";
    private const string SortedDictionaryTypeName =
        "System.Collections.Generic.SortedDictionary<TKey, TValue>";

    public static bool TryGetDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        if (ImmutableSortedSetBuilderCapacityPolicy.IsUnboundedUnionWith(method, typeName))
        {
            forbidden = ImmutableSortedSetBuilderCapacityPolicy.UnionWithDisplayName;
            return true;
        }

        if (ImmutableHashSetBuilderCapacityPolicy.IsUnionWith(method, typeName))
        {
            forbidden = ImmutableHashSetBuilderCapacityPolicy.UnionWithDisplayName;
            return true;
        }

        if (ImmutableSortedDictionaryBuilderPolicy.IsAddRange(method, typeName))
        {
            forbidden = ImmutableSortedDictionaryBuilderPolicy.AddRangeDisplayName;
            return true;
        }

        if (method is { IsStatic: false, Name: "EnqueueRange" } &&
            string.Equals(typeName, PriorityQueueTypeName, StringComparison.Ordinal))
        {
            forbidden = "System.Collections.Generic.PriorityQueue.EnqueueRange";
            return true;
        }

        if (method is { IsStatic: false, Name: "AddRange" or "InsertRange" } &&
            string.Equals(typeName, ListTypeName, StringComparison.Ordinal))
        {
            forbidden = $"System.Collections.Generic.List.{method.Name}";
            return true;
        }

        if (IsSortedDictionaryCopyConstructor(method, typeName))
        {
            forbidden = "System.Collections.Generic.SortedDictionary";
            return true;
        }

        if (IsNonGenericSortedListDictionaryCopyConstructor(method, typeName))
        {
            forbidden = NonGenericSortedListTypeName;
            return true;
        }

        forbidden = null!;
        return false;
    }

    private static bool IsSortedDictionaryCopyConstructor(IMethodSymbol method, string typeName)
        => method.MethodKind == MethodKind.Constructor &&
           string.Equals(typeName, SortedDictionaryTypeName, StringComparison.Ordinal) &&
           method.Parameters.Any(parameter => string.Equals(
               parameter.Type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
               DictionaryInterfaceTypeName,
               StringComparison.Ordinal));

    private static bool IsNonGenericSortedListDictionaryCopyConstructor(
        IMethodSymbol method,
        string typeName)
        => method.MethodKind == MethodKind.Constructor &&
           string.Equals(typeName, NonGenericSortedListTypeName, StringComparison.Ordinal) &&
           method.Parameters.Any(parameter => string.Equals(
               parameter.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
               NonGenericDictionaryInterfaceTypeName,
               StringComparison.Ordinal));
}

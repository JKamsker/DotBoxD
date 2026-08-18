using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class CollectionBulkGrowthPolicy
{
    private const string PriorityQueueTypeName =
        "System.Collections.Generic.PriorityQueue<TElement, TPriority>";

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

        forbidden = null!;
        return false;
    }
}

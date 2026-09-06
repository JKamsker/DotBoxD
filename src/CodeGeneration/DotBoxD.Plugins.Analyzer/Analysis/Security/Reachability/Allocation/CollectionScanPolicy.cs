using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class CollectionScanPolicy
{
    private const string DictionaryTypeName = "System.Collections.Generic.Dictionary<TKey, TValue>";
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string QueueTypeName = "System.Collections.Generic.Queue<T>";
    private const string PriorityQueueTypeName =
        "System.Collections.Generic.PriorityQueue<TElement, TPriority>";
    private const string SortedListTypeName = "System.Collections.Generic.SortedList<TKey, TValue>";

    public static bool TryGetDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        if (method is { IsStatic: false, Name: "TrueForAll" } &&
            string.Equals(typeName, ListTypeName, StringComparison.Ordinal))
        {
            forbidden = "System.Collections.Generic.List.TrueForAll";
            return true;
        }

        if (method is { IsStatic: false, Name: "TrimExcess" } &&
            string.Equals(typeName, DictionaryTypeName, StringComparison.Ordinal))
        {
            forbidden = "System.Collections.Generic.Dictionary.TrimExcess";
            return true;
        }

        if (method is { IsStatic: false, Name: "TrimExcess" } &&
            string.Equals(typeName, QueueTypeName, StringComparison.Ordinal))
        {
            forbidden = "System.Collections.Generic.Queue.TrimExcess";
            return true;
        }

        if (method is { IsStatic: false, MethodKind: MethodKind.Ordinary, Name: "TrimExcess" } &&
            string.Equals(typeName, PriorityQueueTypeName, StringComparison.Ordinal))
        {
            forbidden = "System.Collections.Generic.PriorityQueue.TrimExcess";
            return true;
        }

        if (method is { IsStatic: false, Name: "TrimExcess" } &&
            string.Equals(typeName, SortedListTypeName, StringComparison.Ordinal))
        {
            forbidden = "System.Collections.Generic.SortedList.TrimExcess";
            return true;
        }

        forbidden = null!;
        return false;
    }
}

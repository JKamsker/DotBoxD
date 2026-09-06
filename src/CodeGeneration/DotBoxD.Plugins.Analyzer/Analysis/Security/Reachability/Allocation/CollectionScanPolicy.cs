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

        return TryGetTrimExcessDisplayName(method, typeName, out forbidden);
    }

    private static bool TryGetTrimExcessDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        if (method is not { IsStatic: false, Name: "TrimExcess" } ||
            (method.MethodKind != MethodKind.Ordinary &&
             string.Equals(typeName, PriorityQueueTypeName, StringComparison.Ordinal)))
        {
            forbidden = null!;
            return false;
        }

        forbidden = typeName switch
        {
            DictionaryTypeName => "System.Collections.Generic.Dictionary.TrimExcess",
            QueueTypeName => "System.Collections.Generic.Queue.TrimExcess",
            PriorityQueueTypeName => "System.Collections.Generic.PriorityQueue.TrimExcess",
            SortedListTypeName => "System.Collections.Generic.SortedList.TrimExcess",
            _ => null!
        };
        return forbidden is not null;
    }
}

using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class CollectionScanPolicy
{
    private const string DictionaryTypeName = "System.Collections.Generic.Dictionary<TKey, TValue>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string IReadOnlySetTypeName = "System.Collections.Generic.IReadOnlySet<T>";
    private const string SetInterfaceTypeName = "System.Collections.Generic.ISet<T>";
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string QueueTypeName = "System.Collections.Generic.Queue<T>";
    private const string PriorityQueueTypeName =
        "System.Collections.Generic.PriorityQueue<TElement, TPriority>";
    private const string SortedListTypeName = "System.Collections.Generic.SortedList<TKey, TValue>";

    public static bool TryGetDisplayName(IMethodSymbol method, string typeName, out string forbidden)
    {
        forbidden = method.IsStatic ? null! : GetDisplayName(method, typeName)!;
        return forbidden is not null;
    }

    private static string? GetDisplayName(IMethodSymbol method, string typeName)
        => (typeName, method.Name, method.MethodKind) switch
        {
            (DictionaryTypeName, "TrimExcess", _) => "System.Collections.Generic.Dictionary.TrimExcess",
            (HashSetTypeName, _, _) => GetHashSetDisplayName(method.Name),
            (IReadOnlySetTypeName, "SetEquals", _) => "System.Collections.Generic.IReadOnlySet.SetEquals",
            (SetInterfaceTypeName, "SetEquals", _) => "System.Collections.Generic.ISet.SetEquals",
            (ListTypeName, "TrueForAll", _) => "System.Collections.Generic.List.TrueForAll",
            (QueueTypeName, "TrimExcess", _) => "System.Collections.Generic.Queue.TrimExcess",
            (PriorityQueueTypeName, "TrimExcess", MethodKind.Ordinary) =>
                "System.Collections.Generic.PriorityQueue.TrimExcess",
            (SortedListTypeName, "TrimExcess", _) => "System.Collections.Generic.SortedList.TrimExcess",
            _ => null
        };

    private static string? GetHashSetDisplayName(string methodName)
        => methodName switch
        {
            "IsProperSubsetOf" => "System.Collections.Generic.HashSet.IsProperSubsetOf",
            "IsSupersetOf" => "System.Collections.Generic.HashSet.IsSupersetOf",
            "IsProperSupersetOf" => "System.Collections.Generic.HashSet.IsProperSupersetOf",
            "SetEquals" => "System.Collections.Generic.HashSet.SetEquals",
            _ => null
        };
}

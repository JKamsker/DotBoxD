using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class CollectionSourceConstructorPolicy
{
    private const string ArrayListTypeName = "System.Collections.ArrayList";
    private const string ConcurrentDictionaryTypeName =
        "System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string LinkedListTypeName = "System.Collections.Generic.LinkedList<T>";
    private const string NonGenericQueueTypeName = "System.Collections.Queue";
    private const string NonGenericStackTypeName = "System.Collections.Stack";
    private const string QueueTypeName = "System.Collections.Generic.Queue<T>";
    private const string SortedDictionaryTypeName =
        "System.Collections.Generic.SortedDictionary<TKey, TValue>";
    private const string SortedListTypeName = "System.Collections.Generic.SortedList<TKey, TValue>";
    private const string SortedSetTypeName = "System.Collections.Generic.SortedSet<T>";
    private const string StackTypeName = "System.Collections.Generic.Stack<T>";

    public static bool IsMatch(IMethodSymbol method, string typeName)
        => IsUnboundedEnumerableConstructor(method, typeName) ||
           IsSingleParameterConstructor(method, typeName, ArrayListTypeName) ||
           IsNamedSingleParameterConstructor(method, typeName, NonGenericQueueTypeName, "col") ||
           IsNamedSingleParameterConstructor(method, typeName, NonGenericStackTypeName, "col") ||
           IsNamedSingleParameterConstructor(method, typeName, SortedDictionaryTypeName, "dictionary") ||
           IsSortedListDictionaryConstructor(method, typeName);

    private static bool IsUnboundedEnumerableConstructor(IMethodSymbol method, string typeName)
        => typeName is ConcurrentDictionaryTypeName
            ? HasParameter(method, "collection") || HasParameter(method, "dictionary")
            : typeName is HashSetTypeName or SortedSetTypeName
            ? HasParameter(method, "collection")
            : typeName is LinkedListTypeName
                          or QueueTypeName
                          or StackTypeName &&
              method.Parameters.Length == 1 &&
              string.Equals(method.Parameters[0].Name, "collection", StringComparison.Ordinal);

    private static bool IsSortedListDictionaryConstructor(IMethodSymbol method, string typeName)
        => string.Equals(typeName, SortedListTypeName, StringComparison.Ordinal) &&
           HasParameter(method, "dictionary");

    private static bool IsSingleParameterConstructor(
        IMethodSymbol method,
        string typeName,
        string expectedTypeName)
        => string.Equals(typeName, expectedTypeName, StringComparison.Ordinal) &&
           method.Parameters.Length == 1;

    private static bool IsNamedSingleParameterConstructor(
        IMethodSymbol method,
        string typeName,
        string expectedTypeName,
        string parameterName)
        => IsSingleParameterConstructor(method, typeName, expectedTypeName) &&
           string.Equals(method.Parameters[0].Name, parameterName, StringComparison.Ordinal);

    private static bool HasParameter(IMethodSymbol method, string parameterName)
        => method.Parameters.Any(parameter =>
            string.Equals(parameter.Name, parameterName, StringComparison.Ordinal));
}

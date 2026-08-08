using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionCapacityPolicy
{
    private const string ArrayListTypeName = "System.Collections.ArrayList";
    private const string DictionaryTypeName = "System.Collections.Generic.Dictionary<TKey, TValue>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string PriorityQueueTypeName =
        "System.Collections.Generic.PriorityQueue<TElement, TPriority>";
    private const string QueueTypeName = "System.Collections.Generic.Queue<T>";
    private const string StackTypeName = "System.Collections.Generic.Stack<T>";

    public static bool TryGetDisplayName(IMethodSymbol? method, out string forbidden)
    {
        if (method is not { MethodKind: MethodKind.Constructor } ||
            !HasCapacityParameter(method))
        {
            forbidden = null!;
            return false;
        }

        return TryGetTypeDisplayName(method.ContainingType, out forbidden);
    }

    private static bool TryGetTypeDisplayName(INamedTypeSymbol type, out string forbidden)
    {
        var typeName = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        if (typeName == ArrayListTypeName)
        {
            forbidden = ArrayListTypeName;
            return true;
        }

        if (typeName == ListTypeName)
        {
            forbidden = "System.Collections.Generic.List";
            return true;
        }

        if (typeName == DictionaryTypeName)
        {
            forbidden = "System.Collections.Generic.Dictionary";
            return true;
        }

        if (typeName == QueueTypeName)
        {
            forbidden = "System.Collections.Generic.Queue";
            return true;
        }

        if (typeName is StackTypeName or HashSetTypeName or PriorityQueueTypeName)
        {
            forbidden = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            return true;
        }

        forbidden = null!;
        return false;
    }

    private static bool HasCapacityParameter(IMethodSymbol method)
    {
        var typeName = method.ContainingType.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        var capacityName = typeName == PriorityQueueTypeName ? "initialCapacity" : "capacity";
        return method.Parameters.Any(parameter =>
            parameter.Type.SpecialType == SpecialType.System_Int32 &&
            string.Equals(parameter.Name, capacityName, StringComparison.Ordinal));
    }
}

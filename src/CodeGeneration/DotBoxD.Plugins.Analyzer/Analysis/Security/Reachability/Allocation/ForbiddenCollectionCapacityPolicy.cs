using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionCapacityPolicy
{
    private const string DictionaryTypeName = "System.Collections.Generic.Dictionary<TKey, TValue>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string ImmutableArrayBuilderTypeName =
        "System.Collections.Immutable.ImmutableArray<T>.Builder";
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

        var type = method.ContainingType;
        var typeName = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        forbidden = typeName switch
        {
            ListTypeName => "System.Collections.Generic.List",
            DictionaryTypeName => "System.Collections.Generic.Dictionary",
            QueueTypeName => "System.Collections.Generic.Queue",
            StackTypeName or HashSetTypeName or PriorityQueueTypeName =>
                type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            _ => null!
        };

        return forbidden is not null;
    }

    public static bool TryGetDisplayName(IPropertySymbol? property, out string forbidden)
    {
        if (property is not
            {
                Name: "Capacity",
                SetMethod: not null,
                Type.SpecialType: SpecialType.System_Int32
            })
        {
            forbidden = null!;
            return false;
        }

        var typeName = property.ContainingType.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        forbidden = typeName == ImmutableArrayBuilderTypeName
            ? "System.Collections.Immutable.ImmutableArray"
            : null!;
        return forbidden is not null;
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

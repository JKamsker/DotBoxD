using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionCapacityPolicy
{
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string PriorityQueueTypeName =
        "System.Collections.Generic.PriorityQueue<TElement, TPriority>";

    public static bool TryGetHashSetDisplayName(IMethodSymbol method, out string forbidden)
    {
        if (!TryGetCapacityContainingType(method, HashSetTypeName, requireCapacityName: true, out var type))
        {
            forbidden = null!;
            return false;
        }

        forbidden = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return true;
    }

    public static bool TryGetPriorityQueueType(IMethodSymbol method, out ITypeSymbol forbidden)
    {
        if (!TryGetCapacityContainingType(method, PriorityQueueTypeName, requireCapacityName: false, out var type))
        {
            forbidden = null!;
            return false;
        }

        forbidden = type;
        return true;
    }

    private static bool TryGetCapacityContainingType(
        IMethodSymbol method,
        string expectedTypeName,
        bool requireCapacityName,
        out INamedTypeSymbol type)
    {
        type = method.ContainingType;
        var parameter = method.Parameters.FirstOrDefault();
        return method.MethodKind == MethodKind.Constructor &&
            string.Equals(
                type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                expectedTypeName,
                StringComparison.Ordinal) &&
            parameter?.Type.SpecialType == SpecialType.System_Int32 &&
            (!requireCapacityName || string.Equals(parameter.Name, "capacity", StringComparison.Ordinal));
    }
}

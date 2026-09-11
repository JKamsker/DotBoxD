using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionScanPolicy
{
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string StackTypeName = "System.Collections.Generic.Stack<T>";

    public static bool TryGetDisplayName(IMethodSymbol method, out string forbidden)
    {
        if (method is not { IsStatic: false, MethodKind: MethodKind.Ordinary })
        {
            forbidden = null!;
            return false;
        }

        var typeName = method.ContainingType.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (IsForbiddenListScan(method.Name, typeName))
        {
            forbidden = $"System.Collections.Generic.List.{method.Name}";
            return true;
        }

        if (IsHashSetOverlaps(method.Name, typeName))
        {
            forbidden = "System.Collections.Generic.HashSet.Overlaps";
            return true;
        }

        if (IsStackTrimExcess(method.Name, typeName))
        {
            forbidden = "System.Collections.Generic.Stack.TrimExcess";
            return true;
        }

        forbidden = null!;
        return false;
    }

    private static bool IsForbiddenListScan(string methodName, string typeName)
        => methodName is "BinarySearch" or "Clear" or "Contains" or "IndexOf" or "Remove" &&
           string.Equals(typeName, ListTypeName, StringComparison.Ordinal);

    private static bool IsHashSetOverlaps(string methodName, string typeName)
        => methodName == "Overlaps" && string.Equals(typeName, HashSetTypeName, StringComparison.Ordinal);

    private static bool IsStackTrimExcess(string methodName, string typeName)
        => methodName == "TrimExcess" && string.Equals(typeName, StackTypeName, StringComparison.Ordinal);
}

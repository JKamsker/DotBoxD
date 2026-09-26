using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionScanPolicy
{
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string ReadOnlySetInterfaceTypeName = "System.Collections.Generic.IReadOnlySet<T>";
    private const string SortedSetTypeName = "System.Collections.Generic.SortedSet<T>";
    private const string SetInterfaceTypeName = "System.Collections.Generic.ISet<T>";
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

        if (IsForbiddenSetScan(method.Name, typeName))
        {
            forbidden = $"System.Collections.Generic.{SetCollectionType(typeName)}.{method.Name}";
            return true;
        }

        if (IsISetIsSupersetOf(method.Name, typeName))
        {
            forbidden = "System.Collections.Generic.ISet.IsSupersetOf";
            return true;
        }

        if (IsSetOverlaps(method.Name, typeName))
        {
            forbidden = $"System.Collections.Generic.{SetCollectionType(typeName)}.Overlaps";
            return true;
        }

        if (IsSetProperSubsetOf(method.Name, typeName))
        {
            forbidden = $"System.Collections.Generic.{SetCollectionType(typeName)}.IsProperSubsetOf";
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

    private static bool IsForbiddenSetScan(string methodName, string typeName)
        => methodName is "IsSubsetOf" or "IsProperSupersetOf" &&
           (string.Equals(typeName, HashSetTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, ReadOnlySetInterfaceTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, SortedSetTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, SetInterfaceTypeName, StringComparison.Ordinal));

    private static bool IsISetIsSupersetOf(string methodName, string typeName)
        => methodName == "IsSupersetOf" && string.Equals(typeName, SetInterfaceTypeName, StringComparison.Ordinal);

    private static bool IsSetOverlaps(string methodName, string typeName)
        => methodName == "Overlaps" &&
           (string.Equals(typeName, HashSetTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, ReadOnlySetInterfaceTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, SortedSetTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, SetInterfaceTypeName, StringComparison.Ordinal));

    private static string SetCollectionType(string typeName)
        => typeName switch
        {
            ReadOnlySetInterfaceTypeName => "IReadOnlySet",
            SetInterfaceTypeName => "ISet",
            SortedSetTypeName => "SortedSet",
            _ => "HashSet",
        };

    private static bool IsSetProperSubsetOf(string methodName, string typeName)
        => methodName == "IsProperSubsetOf" &&
           (string.Equals(typeName, HashSetTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, ReadOnlySetInterfaceTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, SortedSetTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, SetInterfaceTypeName, StringComparison.Ordinal));

    private static bool IsStackTrimExcess(string methodName, string typeName)
        => methodName == "TrimExcess" && string.Equals(typeName, StackTypeName, StringComparison.Ordinal);
}

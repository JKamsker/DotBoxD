using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionScanPolicy
{
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string CollectionInterfaceTypeName = "System.Collections.Generic.ICollection<T>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string DictionaryTypeName = "System.Collections.Generic.Dictionary<TKey, TValue>";
    private const string HashtableTypeName = "System.Collections.Hashtable";
    private const string ListInterfaceTypeName = "System.Collections.Generic.IList<T>";
    private const string ReadOnlySetInterfaceTypeName = "System.Collections.Generic.IReadOnlySet<T>";
    private const string SortedSetTypeName = "System.Collections.Generic.SortedSet<T>";
    private const string SetInterfaceTypeName = "System.Collections.Generic.ISet<T>";
    private const string StackTypeName = "System.Collections.Generic.Stack<T>";

    public static bool TryGetDisplayName(IInvocationOperation invocation, Compilation compilation, out string forbidden)
    {
        var method = invocation.TargetMethod;
        if (!FrameworkCollectionIdentity.IsFrameworkType(method.ContainingType))
        {
            forbidden = null!;
            return false;
        }

        var typeName = method.ContainingType.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (EnumerableScanPolicy.IsScan(invocation, typeName))
        {
            forbidden = $"System.Linq.Enumerable.{method.Name}";
            return true;
        }

        if (method is not { IsStatic: false, MethodKind: MethodKind.Ordinary })
        {
            forbidden = null!;
            return false;
        }

        if (IsForbiddenListScan(method.Name, typeName))
        {
            forbidden = $"System.Collections.Generic.List.{method.Name}";
            return true;
        }

        if (IsDictionaryValueScan(method.Name, typeName))
        {
            forbidden = "System.Collections.Generic.Dictionary.ContainsValue";
            return true;
        }

        if (IsForbiddenHashtableScan(method.Name, typeName))
        {
            forbidden = "System.Collections.Hashtable.ContainsValue";
            return true;
        }

        if (IsSortedListValuesScan(method, typeName, invocation.Instance, compilation))
        {
            forbidden = $"System.Collections.Generic.{CollectionType(typeName)}.{method.Name}";
            return true;
        }

        if (TryGetSetDisplayName(method, typeName, out forbidden))
        {
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

    private static bool TryGetSetDisplayName(
        IMethodSymbol method,
        string typeName,
        out string forbidden)
    {
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

        if (IsSetProperSubsetOf(method, typeName))
        {
            forbidden = $"System.Collections.Generic.{SetCollectionType(typeName)}.IsProperSubsetOf";
            return true;
        }

        forbidden = null!;
        return false;
    }

    private static bool IsForbiddenListScan(string methodName, string typeName)
        => methodName is "BinarySearch" or "Clear" or "Contains" or "IndexOf" or "Remove" &&
           string.Equals(typeName, ListTypeName, StringComparison.Ordinal);

    private static bool IsDictionaryValueScan(string methodName, string typeName)
        => methodName == "ContainsValue" &&
           string.Equals(typeName, DictionaryTypeName, StringComparison.Ordinal);

    private static bool IsForbiddenHashtableScan(string methodName, string typeName)
        => methodName == "ContainsValue" && string.Equals(typeName, HashtableTypeName, StringComparison.Ordinal);

    private static bool IsSortedListValuesScan(
        IMethodSymbol method,
        string typeName,
        IOperation? instance,
        Compilation compilation)
        => IsSortedListValuesScanMethod(method.Name, typeName) &&
           SortedListValuesOrigin.IsMatch(instance, compilation);

    private static bool IsSortedListValuesScanMethod(string methodName, string typeName)
        => (methodName == "Contains" && string.Equals(typeName, CollectionInterfaceTypeName, StringComparison.Ordinal)) ||
           (methodName == "IndexOf" && string.Equals(typeName, ListInterfaceTypeName, StringComparison.Ordinal));

    private static string CollectionType(string typeName)
        => string.Equals(typeName, ListInterfaceTypeName, StringComparison.Ordinal) ? "IList" : "ICollection";

    private static bool IsForbiddenSetScan(string methodName, string typeName)
        => methodName is "IsSubsetOf" or "IsSupersetOf" or "IsProperSupersetOf" &&
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

    private static bool IsSetProperSubsetOf(IMethodSymbol method, string typeName)
        => method.Name == "IsProperSubsetOf" &&
           (string.Equals(typeName, HashSetTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, ReadOnlySetInterfaceTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, SortedSetTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, SetInterfaceTypeName, StringComparison.Ordinal));

    private static bool IsStackTrimExcess(string methodName, string typeName)
        => methodName == "TrimExcess" && string.Equals(typeName, StackTypeName, StringComparison.Ordinal);
}

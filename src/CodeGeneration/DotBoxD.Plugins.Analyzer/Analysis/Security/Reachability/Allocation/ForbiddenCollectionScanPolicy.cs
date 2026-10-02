using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionScanPolicy
{
    private const string EnumerableTypeName = "System.Linq.Enumerable";
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string CollectionInterfaceTypeName = "System.Collections.Generic.ICollection<T>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string DictionaryTypeName = "System.Collections.Generic.Dictionary<TKey, TValue>";
    private const string HashtableTypeName = "System.Collections.Hashtable";
    private const string ListInterfaceTypeName = "System.Collections.Generic.IList<T>";
    private const string ReadOnlySetInterfaceTypeName = "System.Collections.Generic.IReadOnlySet<T>";
    private const string SortedListTypeName = "System.Collections.Generic.SortedList<TKey, TValue>";
    private const string SortedSetTypeName = "System.Collections.Generic.SortedSet<T>";
    private const string SortedSetMetadataName = "System.Collections.Generic.SortedSet`1";
    private const string SetInterfaceTypeName = "System.Collections.Generic.ISet<T>";
    private const string StackTypeName = "System.Collections.Generic.Stack<T>";

    public static bool TryGetDisplayName(IInvocationOperation invocation, Compilation compilation, out string forbidden)
    {
        var method = invocation.TargetMethod;
        var typeName = method.ContainingType.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (IsForbiddenEnumerableScan(method, typeName))
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

        if (IsSortedListValuesScan(method, typeName, invocation.Instance))
        {
            forbidden = $"System.Collections.Generic.{CollectionType(typeName)}.{method.Name}";
            return true;
        }

        if (TryGetSetDisplayName(method, compilation, typeName, out forbidden))
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
        Compilation compilation,
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

        if (IsSetProperSubsetOf(method, compilation, typeName))
        {
            forbidden = $"System.Collections.Generic.{SetCollectionType(typeName)}.IsProperSubsetOf";
            return true;
        }

        forbidden = null!;
        return false;
    }

    private static bool IsForbiddenEnumerableScan(IMethodSymbol method, string typeName)
        => method is { IsStatic: true, MethodKind: MethodKind.Ordinary } &&
           method.Name is "Contains" or "Any" &&
           string.Equals(typeName, EnumerableTypeName, StringComparison.Ordinal);

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
        IOperation? instance)
        => IsSortedListValuesScanMethod(method.Name, typeName) &&
           instance is IPropertyReferenceOperation
           {
               Property.Name: "Values",
               Property.ContainingType.OriginalDefinition: { } containingType
           } &&
           string.Equals(
               containingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
               SortedListTypeName,
               StringComparison.Ordinal);

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

    private static bool IsSetProperSubsetOf(IMethodSymbol method, Compilation compilation, string typeName)
        => method.Name == "IsProperSubsetOf" &&
           (string.Equals(typeName, HashSetTypeName, StringComparison.Ordinal) ||
            string.Equals(typeName, ReadOnlySetInterfaceTypeName, StringComparison.Ordinal) ||
            IsFrameworkSortedSet(method.ContainingType, compilation) ||
            string.Equals(typeName, SetInterfaceTypeName, StringComparison.Ordinal));

    private static bool IsFrameworkSortedSet(INamedTypeSymbol type, Compilation compilation)
    {
        var frameworkAssembly = compilation.References
            .Select(compilation.GetAssemblyOrModuleSymbol)
            .OfType<IAssemblySymbol>()
            .FirstOrDefault(assembly => string.Equals(
                assembly.Identity.Name,
                "System.Collections",
                StringComparison.Ordinal));
        var frameworkSortedSet = frameworkAssembly?.GetTypeByMetadataName(SortedSetMetadataName);
        return frameworkSortedSet is not null &&
               SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, frameworkSortedSet);
    }

    private static bool IsStackTrimExcess(string methodName, string typeName)
        => methodName == "TrimExcess" && string.Equals(typeName, StackTypeName, StringComparison.Ordinal);
}

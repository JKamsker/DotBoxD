using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenCollectionScanPolicy
{
    private const string ListTypeName = "System.Collections.Generic.List<T>";
    private const string HashSetTypeName = "System.Collections.Generic.HashSet<T>";
    private const string ReadOnlySetInterfaceTypeName = "System.Collections.Generic.IReadOnlySet<T>";
    private const string SortedSetTypeName = "System.Collections.Generic.SortedSet<T>";
    private const string SortedSetMetadataName = "System.Collections.Generic.SortedSet`1";
    private const string SetInterfaceTypeName = "System.Collections.Generic.ISet<T>";
    private const string StackTypeName = "System.Collections.Generic.Stack<T>";

    public static bool TryGetDisplayName(IMethodSymbol method, Compilation compilation, out string forbidden)
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

        if (IsSetProperSubsetOf(method, compilation, typeName))
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

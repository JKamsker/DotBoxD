using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class EnumerableScanPolicy
{
    private static readonly HashSet<string> ScanMethodNames = new(StringComparer.Ordinal)
    {
        "Contains", "Any", "Count", "SequenceEqual", "All", "LongCount", "FirstOrDefault", "LastOrDefault",
        "SingleOrDefault", "Single", "Aggregate", "First", "Max", "Min", "Sum", "Average", "MaxBy",
        "ElementAt", "ElementAtOrDefault", "Skip", "Last"
    };

    public static bool IsScan(IInvocationOperation invocation, string typeName, Compilation compilation)
    {
        var method = invocation.TargetMethod;
        if (method is not { IsStatic: true, MethodKind: MethodKind.Ordinary } ||
            !IsScanMethod(method) ||
            typeName != "System.Linq.Enumerable")
        {
            return false;
        }

        var source = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
        while (source is IConversionOperation { IsImplicit: true, OperatorMethod: null } conversion)
        {
            source = conversion.Operand;
        }

        return !IsBoundedCollectionCall(method, source, compilation);
    }

    public static bool IsUnboundedMethodReference(IMethodSymbol method, string typeName)
        => typeName == "System.Linq.Enumerable" && method is { IsStatic: true, MethodKind: MethodKind.Ordinary } &&
           IsScanMethod(method);

    private static bool IsScanMethod(IMethodSymbol method) => ScanMethodNames.Contains(method.Name);

    private static bool IsBoundedCollectionCall(IMethodSymbol method, IOperation? source, Compilation compilation)
    {
        if (method.Name == "Contains" && !HasCompatibleCollectionElementType(method, source))
        {
            return false;
        }

        if (IsBoundedCountCall(method, source))
        {
            return true;
        }

        if (IsBoundedSortedListContains(method, source))
        {
            return true;
        }

        if (source?.Type is IArrayTypeSymbol)
        {
            return IsBoundedFrameworkCall(method, source, string.Empty);
        }

        if (source?.Type is not INamedTypeSymbol type || !FrameworkCollectionIdentity.IsFrameworkType(type))
        {
            return false;
        }

        var name = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return IsBoundedFrameworkCall(method, source, name) && FrameworkCollectionOrigin.IsExact(source, compilation);
    }

    private static bool IsBoundedCountCall(IMethodSymbol method, IOperation? source)
        => method.Name is "Any" or "Count" && method.Parameters.Length == 1 &&
           (source?.Type is IArrayTypeSymbol || IsSortedListView(source));

    private static bool HasCompatibleCollectionElementType(IMethodSymbol method, IOperation? source)
        => source?.Type is INamedTypeSymbol type &&
           type.AllInterfaces.Any(interfaceType =>
               interfaceType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_ICollection_T &&
               FrameworkCollectionIdentity.IsFrameworkType(interfaceType) &&
               SymbolEqualityComparer.Default.Equals(interfaceType.TypeArguments[0], method.TypeArguments[0]));

    private static bool HasCompatibleCountFastPath(IMethodSymbol method, IOperation? source)
        => HasCompatibleCollectionElementType(method, source) ||
           source?.Type is INamedTypeSymbol type && type.AllInterfaces.Any(interfaceType =>
               FrameworkCollectionIdentity.IsFrameworkType(interfaceType) &&
               interfaceType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ==
               "System.Collections.ICollection");

    private static bool IsBoundedSortedListContains(IMethodSymbol method, IOperation? source)
        => method.Name == "Contains" && method.Parameters.Length == 2 &&
           source is IPropertyReferenceOperation { Property.Name: "Keys" } && IsSortedListView(source);

    private static bool IsSortedListView(IOperation? source)
        => source is IPropertyReferenceOperation { Property.Name: "Keys" or "Values" } property &&
           FrameworkCollectionIdentity.IsFrameworkType(property.Property.ContainingType) &&
           property.Property.ContainingType.OriginalDefinition.ToDisplayString(
               SymbolDisplayFormat.CSharpErrorMessageFormat) == "System.Collections.Generic.SortedList<TKey, TValue>";

    private static bool IsBoundedFrameworkCall(IMethodSymbol method, IOperation? source, string name)
        => IsCountMethod(method) && HasBoundedCount(name) && HasCompatibleCountFastPath(method, source) ||
           IsBoundedIndexedOrContainsCall(method, source, name);

    private static bool IsCountMethod(IMethodSymbol method)
        => method.Name is "Any" or "Count" && method.Parameters.Length == 1;

    private static bool IsBoundedIndexedOrContainsCall(IMethodSymbol method, IOperation? source, string name)
        => method.Name switch
        {
            "Contains" when method.Parameters.Length == 2 => HasBoundedContains(name),
            "LastOrDefault" when HasNoPredicate(method) && HasCompatibleIndexedFastPath(method, source) => true,
            _ when IsParameterlessIndexedMethod(method) => HasCompatibleIndexedFastPath(method, source),
            _ => false
        };

    private static bool IsParameterlessIndexedMethod(IMethodSymbol method)
        => method.Name is "First" or "Last" or "Single" && method.Parameters.Length == 1;

    private static bool HasNoPredicate(IMethodSymbol method)
        => method.Parameters.All(static parameter => parameter.Type.TypeKind != TypeKind.Delegate);

    private static bool HasCompatibleIndexedFastPath(IMethodSymbol method, IOperation? source)
        => source?.Type switch
        {
            IArrayTypeSymbol array => SymbolEqualityComparer.Default.Equals(array.ElementType, method.TypeArguments[0]),
            INamedTypeSymbol type => IsCompatibleFrameworkList(type, method.TypeArguments[0]),
            _ => false,
        };

    private static bool IsCompatibleFrameworkList(INamedTypeSymbol type, ITypeSymbol elementType)
        => FrameworkCollectionIdentity.IsFrameworkType(type) &&
           type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ==
               "System.Collections.Generic.List<T>" &&
           SymbolEqualityComparer.Default.Equals(type.TypeArguments[0], elementType);

    private static bool HasBoundedCount(string typeName)
        => HasBoundedCollectionCount(typeName) || HasBoundedDictionaryViewCount(typeName);

    private static bool HasBoundedCollectionCount(string typeName)
        => typeName is "System.Collections.Generic.List<T>" or "System.Collections.Generic.HashSet<T>" or
            "System.Collections.Generic.SortedSet<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>" or
            "System.Collections.Generic.SortedList<TKey, TValue>" or "System.Collections.Generic.Queue<T>" or
            "System.Collections.Generic.Stack<T>" or "System.Collections.Generic.LinkedList<T>" or
            "System.Collections.Generic.SortedDictionary<TKey, TValue>";

    private static bool HasBoundedDictionaryViewCount(string typeName)
        => IsDictionaryKeyView(typeName) ||
           typeName is "System.Collections.Generic.Dictionary<TKey, TValue>.ValueCollection" or
               "System.Collections.Generic.SortedDictionary<TKey, TValue>.ValueCollection";

    private static bool HasBoundedContains(string typeName)
        => typeName is "System.Collections.Generic.HashSet<T>" or "System.Collections.Generic.SortedSet<T>" ||
           IsDictionaryKeyView(typeName);

    private static bool IsDictionaryKeyView(string typeName)
        => typeName is "System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection" or
            "System.Collections.Generic.SortedDictionary<TKey, TValue>.KeyCollection";
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class EnumerableScanPolicy
{
    public static bool IsScan(IInvocationOperation invocation, string typeName)
    {
        var method = invocation.TargetMethod;
        if (method is not { IsStatic: true, MethodKind: MethodKind.Ordinary } ||
            method.Name is not ("Contains" or "Any") ||
            typeName != "System.Linq.Enumerable")
        {
            return false;
        }

        var source = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
        while (source is IConversionOperation { IsImplicit: true } conversion)
        {
            source = conversion.Operand;
        }

        return !IsBoundedCollectionCall(method, source?.Type);
    }

    private static bool IsBoundedCollectionCall(IMethodSymbol method, ITypeSymbol? sourceType)
    {
        if (method.Name == "Any" && method.Parameters.Length == 1 && sourceType is IArrayTypeSymbol)
        {
            return true;
        }

        if (sourceType is not INamedTypeSymbol type || !FrameworkCollectionIdentity.IsFrameworkType(type))
        {
            return false;
        }

        var name = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return IsBoundedFrameworkCall(method, name);
    }

    private static bool IsBoundedFrameworkCall(IMethodSymbol method, string name)
        => method.Name switch
        {
            "Any" when method.Parameters.Length == 1 => HasBoundedCount(name),
            "Contains" when method.Parameters.Length == 2 => HasBoundedContains(name),
            _ => false
        };

    private static bool HasBoundedCount(string typeName)
        => typeName is "System.Collections.Generic.List<T>" or "System.Collections.Generic.HashSet<T>" or
            "System.Collections.Generic.SortedSet<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>" or
            "System.Collections.Generic.SortedList<TKey, TValue>";

    private static bool HasBoundedContains(string typeName)
        => typeName is "System.Collections.Generic.HashSet<T>" or "System.Collections.Generic.SortedSet<T>";
}

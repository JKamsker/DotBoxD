using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal static class MergeableIrErrorObsoleteTypeValidator
{
    public static string? TryGetUnsupportedTypeName(
        MergeableIrMarkedLoweringCall call,
        Compilation compilation)
    {
        var obsoleteAttribute = compilation.GetTypeByMetadataName("System.ObsoleteAttribute");
        if (obsoleteAttribute is null)
        {
            return null;
        }

        return FindErrorObsoleteType(call.InputType, obsoleteAttribute)?.Name ??
               FindErrorObsoleteType(call.OutputType, obsoleteAttribute)?.Name ??
               FindErrorObsoleteType(call.Parameter.Type, obsoleteAttribute)?.Name ??
               FindErrorObsoleteType(call.Method.ReturnType, obsoleteAttribute)?.Name;
    }

    private static INamedTypeSymbol? FindErrorObsoleteType(
        ITypeSymbol type,
        INamedTypeSymbol obsoleteAttribute)
    {
        if (type is IArrayTypeSymbol array)
        {
            return FindErrorObsoleteType(array.ElementType, obsoleteAttribute);
        }

        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        if (IsErrorObsoleteOrNestedInErrorObsolete(named, obsoleteAttribute))
        {
            return named;
        }

        foreach (var typeArgument in named.TypeArguments)
        {
            if (FindErrorObsoleteType(typeArgument, obsoleteAttribute) is { } unsupported)
            {
                return unsupported;
            }
        }

        return null;
    }

    private static bool IsErrorObsoleteOrNestedInErrorObsolete(
        INamedTypeSymbol type,
        INamedTypeSymbol obsoleteAttribute)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, obsoleteAttribute) &&
                    attribute.ConstructorArguments.Length >= 2 &&
                    attribute.ConstructorArguments[1].Value is true)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

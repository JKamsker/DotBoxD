using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class DtoPayloadTypeInspector
{
    public static bool CanInspectMembers(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return false;
        }

        var ns = type.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace || !IsSystemNamespace(ns) || IsSourceDefined(type);
    }

    public static bool IsUserDtoNamespace(INamedTypeSymbol type)
        => type.SpecialType == SpecialType.None && IsUserNamespace(type);

    private static bool IsUserNamespace(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace || !IsSystemNamespace(ns) || IsSourceDefined(type);
    }
    private static bool IsSourceDefined(INamedTypeSymbol type)
    {
        foreach (var location in type.Locations)
        {
            if (location.IsInSource)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSystemNamespace(INamespaceSymbol ns)
    {
        while (!ns.IsGlobalNamespace)
        {
            if (ns.ContainingNamespace.IsGlobalNamespace)
            {
                return ns.Name == "System";
            }

            ns = ns.ContainingNamespace;
        }

        return false;
    }
}

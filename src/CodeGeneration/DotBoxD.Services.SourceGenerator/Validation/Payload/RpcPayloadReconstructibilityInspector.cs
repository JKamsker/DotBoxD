using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class RpcPayloadReconstructibilityInspector
{
    public static string? GetUnsupportedReason(
        ITypeSymbol type,
        string role,
        CancellationToken ct) =>
        Inspect(type,
            role,
            ct,
            new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default),
            requireConstructible: true);

    private static string? Inspect(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        bool requireConstructible)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IArrayTypeSymbol array)
        {
            return Inspect(array.ElementType, role, ct, visitedOriginalDefinitions, requireConstructible: true);
        }

        if (type is not INamedTypeSymbol named)
        {
            return null;
        }
        return InspectNamed(
            named,
            role,
            ct,
            visitedOriginalDefinitions,
            requireConstructible);
    }

    private static string? InspectNamed(
        INamedTypeSymbol named,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        bool requireConstructible)
    {
        return InspectTypeArguments(named, role, ct, visitedOriginalDefinitions) ??
               InspectConstructibility(named, role, ct, visitedOriginalDefinitions, requireConstructible) ??
               InspectDtoGraph(named, role, ct, visitedOriginalDefinitions);
    }

    private static string? InspectTypeArguments(
        INamedTypeSymbol named,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        foreach (var arg in named.TypeArguments)
        {
            var argumentReason = Inspect(arg, role, ct, visitedOriginalDefinitions, true);
            if (argumentReason is not null)
            {
                return argumentReason;
            }
        }

        return null;
    }

    private static string? InspectConstructibility(
        INamedTypeSymbol named,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        bool requireConstructible)
        => requireConstructible
            ? GetNonConstructibleDtoReason(named, role, ct, visitedOriginalDefinitions)
            : null;

    private static string? InspectDtoGraph(
        INamedTypeSymbol named,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        if (!CanInspectDtoMembers(named) || !visitedOriginalDefinitions.Add(named.OriginalDefinition))
        {
            return null;
        }

        try
        {
            // DTO construction belongs to the configured ISerializer. Only inspect the graph for
            // RPC-specific payload restrictions and explicit union metadata here; setter and
            // constructor requirements vary between serializers.
            return InspectDtoMembers(named, role, ct, visitedOriginalDefinitions) ??
                   InspectBaseType(named, role, ct, visitedOriginalDefinitions);
        }
        finally
        {
            visitedOriginalDefinitions.Remove(named.OriginalDefinition);
        }
    }

    private static string? InspectDtoMembers(
        INamedTypeSymbol named,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        foreach (var member in named.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            var reason = member switch
            {
                IPropertySymbol property => InspectProperty(property, role, ct, visitedOriginalDefinitions),
                IFieldSymbol field => InspectField(field, role, ct, visitedOriginalDefinitions),
                _ => null,
            };
            if (reason is not null)
            {
                return reason;
            }
        }

        return null;
    }

    private static string? InspectBaseType(
        INamedTypeSymbol named,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
        => named.BaseType is null
            ? null
            : Inspect(named.BaseType, role, ct, visitedOriginalDefinitions, requireConstructible: false);

    private static string? InspectProperty(
        IPropertySymbol property,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        if (property.IsStatic ||
            property.Parameters.Length != 0 ||
            property.DeclaredAccessibility != Accessibility.Public ||
            RpcPayloadIgnoredMember.IsIgnored(property))
        {
            return null;
        }

        return Inspect(
            property.Type,
            $"{role} member '{property.Name}'",
            ct,
            visitedOriginalDefinitions,
            requireConstructible: true);
    }

    private static string? InspectField(
        IFieldSymbol field,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        if (field.IsStatic ||
            field.IsImplicitlyDeclared ||
            field.DeclaredAccessibility != Accessibility.Public ||
            RpcPayloadIgnoredMember.IsIgnored(field))
        {
            return null;
        }

        return Inspect(
            field.Type,
            $"{role} member '{field.Name}'",
            ct,
            visitedOriginalDefinitions,
            requireConstructible: true);
    }

    private static string? GetNonConstructibleDtoReason(
        INamedTypeSymbol type,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        if (!IsUserDtoNamespace(type) || HasRpcServiceAttribute(type, ct))
        {
            return null;
        }

        var defaultReason = type.TypeKind switch
        {
            TypeKind.Interface =>
                $"{role} uses interface DTO '{type.ToDisplayString()}'; RPC payload DTOs must be concrete so the wire contract can be reconstructed.",
            TypeKind.Class when type.IsAbstract =>
                $"{role} uses abstract DTO '{type.ToDisplayString()}'; RPC payload DTOs must be concrete so the wire contract can be reconstructed.",
            _ => null,
        };
        if (defaultReason is null)
        {
            return null;
        }
        return RpcPayloadUnionResolver.TryRead(type, role, ct, out var cases, out var unionReason)
            ? unionReason ?? RpcPayloadUnionCaseInspector.Inspect(
                type,
                cases,
                role,
                ct,
                visitedOriginalDefinitions,
                Inspect)
            : defaultReason;
    }

    private static bool CanInspectDtoMembers(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return false;
        }
        return IsUserDtoNamespace(type);
    }

    private static bool IsUserDtoNamespace(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None)
        {
            return false;
        }

        var ns = type.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace || !IsSystemNamespace(ns);
    }

    private static bool HasRpcServiceAttribute(INamedTypeSymbol type, CancellationToken ct)
    {
        foreach (var attr in type.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (ServicesGeneratorTypeNames.IsRpcServiceAttribute(attr.AttributeClass?.ToDisplayString()))
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

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class RpcPayloadReconstructibilityInspector
{
    public static string? GetUnsupportedReason(
        ITypeSymbol type,
        string role,
        CancellationToken ct) =>
        Inspect(type, role, ct, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));

    private static string? Inspect(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        ct.ThrowIfCancellationRequested();

        if (type is IArrayTypeSymbol array)
        {
            return Inspect(array.ElementType, role, ct, visitedOriginalDefinitions);
        }

        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        foreach (var arg in named.TypeArguments)
        {
            var argumentReason = Inspect(arg, role, ct, visitedOriginalDefinitions);
            if (argumentReason is not null)
            {
                return argumentReason;
            }
        }

        if (!CanInspectDtoMembers(named) || !visitedOriginalDefinitions.Add(named.OriginalDefinition))
        {
            return null;
        }

        try
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

            return named.BaseType is null
                ? null
                : Inspect(named.BaseType, role, ct, visitedOriginalDefinitions);
        }
        finally
        {
            visitedOriginalDefinitions.Remove(named.OriginalDefinition);
        }
    }

    private static string? InspectProperty(
        IPropertySymbol property,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        if (property.IsStatic ||
            property.Parameters.Length != 0 ||
            property.DeclaredAccessibility != Accessibility.Public)
        {
            return null;
        }

        if (property.GetMethod?.DeclaredAccessibility != Accessibility.Public)
        {
            return $"{role} member '{property.Name}' must expose a public getter so RPC payloads can be reconstructed";
        }

        if (property.SetMethod?.DeclaredAccessibility != Accessibility.Public &&
            !HasConstructorParameter(property.ContainingType, property.Name))
        {
            return $"{role} member '{property.Name}' must expose a public setter or init, or match a public constructor parameter, so RPC payloads can be reconstructed";
        }

        return Inspect(property.Type, $"{role} member '{property.Name}'", ct, visitedOriginalDefinitions);
    }

    private static string? InspectField(
        IFieldSymbol field,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions)
    {
        if (field.IsStatic ||
            field.IsImplicitlyDeclared ||
            field.DeclaredAccessibility != Accessibility.Public)
        {
            return null;
        }

        if (field.IsReadOnly && !HasConstructorParameter(field.ContainingType, field.Name))
        {
            return $"{role} member '{field.Name}' is readonly and does not match a public constructor parameter; RPC DTO fields must be reconstructible";
        }

        return Inspect(field.Type, $"{role} member '{field.Name}'", ct, visitedOriginalDefinitions);
    }

    private static bool HasConstructorParameter(INamedTypeSymbol type, string memberName)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public ||
                constructor.IsStatic)
            {
                continue;
            }

            foreach (var parameter in constructor.Parameters)
            {
                if (string.Equals(parameter.Name, memberName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CanInspectDtoMembers(INamedTypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return false;
        }

        var ns = type.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace || !IsSystemNamespace(ns);
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

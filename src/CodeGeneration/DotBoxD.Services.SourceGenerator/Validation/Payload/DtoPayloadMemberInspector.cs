using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal delegate bool DtoPayloadTypePredicate(
    ITypeSymbol type,
    CancellationToken ct,
    HashSet<INamedTypeSymbol> visitedOriginalDefinitions);

internal delegate string? DtoPayloadTypeReasonSelector(
    ITypeSymbol type,
    string role,
    CancellationToken ct,
    HashSet<INamedTypeSymbol> visitedOriginalDefinitions);

internal static class DtoPayloadMemberInspector
{
    public static bool ContainsMemberMatching(
        INamedTypeSymbol type,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        DtoPayloadTypePredicate contains)
    {
        if (!DtoPayloadTypeInspector.CanInspectMembers(type) ||
            !visitedOriginalDefinitions.Add(type.OriginalDefinition))
        {
            return false;
        }

        try
        {
            foreach (var member in type.GetMembers())
            {
                ct.ThrowIfCancellationRequested();

                if (MemberType(member) is { } memberType &&
                    contains(memberType, ct, visitedOriginalDefinitions))
                {
                    return true;
                }
            }

            return type.BaseType is not null &&
                ContainsMemberMatching(type.BaseType, ct, visitedOriginalDefinitions, contains);
        }
        finally
        {
            visitedOriginalDefinitions.Remove(type.OriginalDefinition);
        }
    }

    public static string? FindUnsupportedMember(
        INamedTypeSymbol type,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        DtoPayloadTypeReasonSelector getReason)
    {
        if (!DtoPayloadTypeInspector.CanInspectMembers(type) ||
            !visitedOriginalDefinitions.Add(type.OriginalDefinition))
        {
            return null;
        }

        try
        {
            foreach (var member in type.GetMembers())
            {
                ct.ThrowIfCancellationRequested();

                if (MemberType(member) is not { } memberType)
                {
                    continue;
                }

                var memberRole = $"{role} member '{member.Name}'";
                var reason = getReason(memberType, memberRole, ct, visitedOriginalDefinitions);
                if (reason is not null)
                {
                    return reason;
                }
            }

            return type.BaseType is null
                ? null
                : FindUnsupportedMember(type.BaseType, role, ct, visitedOriginalDefinitions, getReason);
        }
        finally
        {
            visitedOriginalDefinitions.Remove(type.OriginalDefinition);
        }
    }

    private static ITypeSymbol? MemberType(ISymbol member)
        => member switch
        {
            IPropertySymbol
            {
                IsStatic: false,
                Parameters.Length: 0,
                DeclaredAccessibility: Accessibility.Public
            } property when !RpcPayloadIgnoredMember.IsIgnored(property) => property.Type,
            IFieldSymbol
            {
                IsStatic: false,
                IsImplicitlyDeclared: false,
                DeclaredAccessibility: Accessibility.Public
            } field when !RpcPayloadIgnoredMember.IsIgnored(field) => field.Type,
            _ => null,
        };

}

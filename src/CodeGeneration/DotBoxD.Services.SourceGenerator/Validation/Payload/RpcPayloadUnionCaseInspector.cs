using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

internal static class RpcPayloadUnionCaseInspector
{
    public delegate string? InspectType(
        ITypeSymbol type,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        bool requireConstructible);

    public static string? Inspect(
        INamedTypeSymbol unionType,
        IReadOnlyList<INamedTypeSymbol> cases,
        string role,
        CancellationToken ct,
        HashSet<INamedTypeSymbol> visitedOriginalDefinitions,
        InspectType inspectType)
    {
        if (!visitedOriginalDefinitions.Add(unionType.OriginalDefinition))
        {
            return null;
        }

        try
        {
            foreach (var unionCase in cases)
            {
                ct.ThrowIfCancellationRequested();
                var reason = inspectType(
                    unionCase,
                    $"{role} union case '{unionCase.ToDisplayString()}'",
                    ct,
                    visitedOriginalDefinitions,
                    requireConstructible: true);
                if (reason is not null)
                {
                    return reason;
                }
            }

            return null;
        }
        finally
        {
            visitedOriginalDefinitions.Remove(unionType.OriginalDefinition);
        }
    }
}

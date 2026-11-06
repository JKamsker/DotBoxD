using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Validation;

/// <summary>
/// Rejects sub-service properties that lead back to the declaring service (<c>IFolder Parent</c>, or
/// <c>IGame.Players</c> + <c>IPlayers.Game</c>). Generated proxies construct property proxies
/// eagerly and the peer <c>Provide*</c> helpers register property implementations recursively, so a
/// cycle would recurse forever at runtime.
/// </summary>
internal static class SubServicePropertyCycleDetector
{
    public static UnsupportedMemberDiagnostic? Find(
        INamedTypeSymbol interfaceSymbol,
        IEnumerable<IPropertySymbol> properties,
        CancellationToken ct)
    {
        foreach (var property in properties)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryGetSubServiceType(property, ct, out var start) ||
                !Reaches(start, interfaceSymbol, ct))
            {
                continue;
            }

            return new UnsupportedMemberDiagnostic(
                $"sub-service property '{property.Name}' leads back to '{interfaceSymbol.Name}'; cyclic sub-service properties are not supported because generated proxies and Provide registration follow sub-service properties eagerly. Return the service from a method instead",
                DiagnosticLocationFactory.FromSymbol(property));
        }

        return null;
    }

    private static bool Reaches(INamedTypeSymbol start, INamedTypeSymbol target, CancellationToken ct)
    {
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<INamedTypeSymbol>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();
            if (SymbolEqualityComparer.Default.Equals(current, target))
            {
                return true;
            }

            if (!visited.Add(current))
            {
                continue;
            }

            PushPropertyTypes(current, pending, ct);
            foreach (var baseInterface in current.AllInterfaces)
            {
                PushPropertyTypes(baseInterface, pending, ct);
            }
        }

        return false;
    }

    private static void PushPropertyTypes(
        INamedTypeSymbol type,
        Stack<INamedTypeSymbol> pending,
        CancellationToken ct)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is IPropertySymbol property && TryGetSubServiceType(property, ct, out var propertyType))
            {
                pending.Push(propertyType);
            }
        }
    }

    private static bool TryGetSubServiceType(
        IPropertySymbol property,
        CancellationToken ct,
        out INamedTypeSymbol type)
    {
        type = null!;
        if (property.IsStatic ||
            property.Parameters.Length != 0 ||
            property.Type is not INamedTypeSymbol namedType ||
            !SubServiceReturnTypeReader.IsRpcServiceInterface(namedType, ct))
        {
            return false;
        }

        type = namedType;
        return true;
    }
}

using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class InheritedTypeAssemblyIdentityComparer
{
    public static bool HasCompatibleReturnShape(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken ct) =>
        left.RefKind == right.RefKind &&
        MethodSignatureFacts.GetCanonicalType(left.ReturnType, left, ct) ==
        MethodSignatureFacts.GetCanonicalType(right.ReturnType, right, ct) &&
        HaveSameIdentities(left.ReturnType, right.ReturnType, ct);

    public static bool HaveSameParameterIdentities(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken ct)
    {
        if (left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (!HaveSameIdentities(left.Parameters[i].Type, right.Parameters[i].Type, ct))
            {
                return false;
            }
        }

        return true;
    }

    public static bool HaveSameIdentities(
        ITypeSymbol left,
        ITypeSymbol right,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (left is ITypeParameterSymbol || right is ITypeParameterSymbol)
        {
            return true;
        }

        return (left, right) switch
        {
            (IArrayTypeSymbol leftArray, IArrayTypeSymbol rightArray) =>
                HaveSameArrayIdentities(leftArray, rightArray, ct),
            (INamedTypeSymbol leftNamed, INamedTypeSymbol rightNamed) =>
                HaveSameNamedTypeIdentities(leftNamed, rightNamed, ct),
            _ => true,
        };
    }

    private static bool HaveSameArrayIdentities(
        IArrayTypeSymbol left,
        IArrayTypeSymbol right,
        CancellationToken ct) =>
        left.Rank == right.Rank &&
        HaveSameIdentities(left.ElementType, right.ElementType, ct);

    private static bool HaveSameNamedTypeIdentities(
        INamedTypeSymbol left,
        INamedTypeSymbol right,
        CancellationToken ct)
    {
        if (!left.ContainingAssembly.Identity.Equals(right.ContainingAssembly.Identity) ||
            left.TypeArguments.Length != right.TypeArguments.Length)
        {
            return false;
        }

        for (var i = 0; i < left.TypeArguments.Length; i++)
        {
            if (!HaveSameIdentities(left.TypeArguments[i], right.TypeArguments[i], ct))
            {
                return false;
            }
        }

        return true;
    }
}

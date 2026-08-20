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

        if (left is IArrayTypeSymbol leftArray && right is IArrayTypeSymbol rightArray)
        {
            return leftArray.Rank == rightArray.Rank &&
                HaveSameIdentities(leftArray.ElementType, rightArray.ElementType, ct);
        }

        if (left is not INamedTypeSymbol leftNamed || right is not INamedTypeSymbol rightNamed)
        {
            return true;
        }

        if (!leftNamed.ContainingAssembly.Identity.Equals(rightNamed.ContainingAssembly.Identity) ||
            leftNamed.TypeArguments.Length != rightNamed.TypeArguments.Length)
        {
            return false;
        }

        for (var i = 0; i < leftNamed.TypeArguments.Length; i++)
        {
            if (!HaveSameIdentities(leftNamed.TypeArguments[i], rightNamed.TypeArguments[i], ct))
            {
                return false;
            }
        }

        return true;
    }
}

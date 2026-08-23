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
            (IPointerTypeSymbol leftPointer, IPointerTypeSymbol rightPointer) =>
                HaveSameIdentities(leftPointer.PointedAtType, rightPointer.PointedAtType, ct),
            (IFunctionPointerTypeSymbol leftFunctionPointer, IFunctionPointerTypeSymbol rightFunctionPointer) =>
                HaveSameFunctionPointerIdentities(leftFunctionPointer, rightFunctionPointer, ct),
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

    private static bool HaveSameFunctionPointerIdentities(
        IFunctionPointerTypeSymbol left,
        IFunctionPointerTypeSymbol right,
        CancellationToken ct)
    {
        if (!HaveSameIdentities(left.Signature.ReturnType, right.Signature.ReturnType, ct) ||
            left.Signature.Parameters.Length != right.Signature.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Signature.Parameters.Length; i++)
        {
            if (left.Signature.Parameters[i].RefKind != right.Signature.Parameters[i].RefKind ||
                !HaveSameIdentities(left.Signature.Parameters[i].Type, right.Signature.Parameters[i].Type, ct))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HaveSameNamedTypeIdentities(
        INamedTypeSymbol left,
        INamedTypeSymbol right,
        CancellationToken ct)
    {
        var leftAssembly = left.ContainingAssembly;
        var rightAssembly = right.ContainingAssembly;
        if (leftAssembly is null ||
            rightAssembly is null ||
            !leftAssembly.Identity.Equals(rightAssembly.Identity) ||
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

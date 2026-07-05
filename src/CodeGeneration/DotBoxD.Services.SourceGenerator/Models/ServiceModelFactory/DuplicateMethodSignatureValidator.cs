using System;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class DuplicateMethodSignatureValidator
{
    public static bool TryGetConflictReason(
        IMethodSymbol existingMethod,
        IMethodSymbol methodSymbol,
        CancellationToken ct,
        out string reason)
    {
        foreach (var check in Checks)
        {
            ct.ThrowIfCancellationRequested();

            if (check.IsCompatible(existingMethod, methodSymbol, ct))
            {
                continue;
            }

            reason = check.GetReason(methodSymbol);
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static readonly CompatibilityCheck[] Checks =
    [
        new(
            static (existing, current, ct) =>
                InheritedMethodDeduplicator.HasCompatibleReturnShape(existing, current, ct),
            static current =>
                $"inherited method '{current.Name}' has the same signature as another method but an incompatible return type"),
        new(
            static (existing, current, _) =>
                InheritedMethodDeduplicator.HasSameParameterRefKinds(existing, current),
            static current =>
                $"inherited method '{current.Name}' has the same signature as another method but incompatible parameter ref kinds"),
        new(
            static (existing, current, _) =>
                InheritedMethodDeduplicator.HasSameParameterDefaults(existing, current),
            static current =>
                $"inherited method '{current.Name}' has the same signature as another method but incompatible optional parameter defaults"),
        new(
            static (existing, current, ct) =>
                MethodSignatureFacts.HaveSameGenericConstraints(existing, current, ct),
            static current =>
                $"inherited generic method '{current.Name}' has the same signature as another method but incompatible generic constraints"),
        new(
            static (existing, current, ct) =>
                InheritedMethodDeduplicator.HasSameNullableAnnotations(existing, current, ct),
            static current =>
                $"inherited method '{current.Name}' has the same signature as another method but incompatible nullable annotations"),
        new(
            static (existing, current, ct) =>
                TupleElementNameComparer.HasSameElementNames(existing, current, ct),
            static current =>
                $"inherited method '{current.Name}' has the same signature as another method but incompatible tuple element names"),
        new(
            static (existing, current, _) =>
                InheritedMethodDeduplicator.HasSameEffectiveWireName(existing, current),
            static current =>
                $"inherited method '{current.Name}' has the same signature as another method but a different wire method name"),
    ];

    private readonly record struct CompatibilityCheck(
        Func<IMethodSymbol, IMethodSymbol, CancellationToken, bool> IsCompatible,
        Func<IMethodSymbol, string> GetReason);
}

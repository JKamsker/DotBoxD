using System.Collections.Generic;
using System.Threading;
using DotBoxD.CodeGeneration.Shared.Defaults;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class InheritedMethodDeduplicator
{
    public static string? GetDuplicateSignatureRejectionReason(
        IMethodSymbol existingMethod,
        IMethodSymbol methodSymbol, Compilation compilation,
        CancellationToken ct)
    {
        var shapeReason = GetShapeRejectionReason(existingMethod, methodSymbol, ct);
        return shapeReason ?? GetContractRejectionReason(existingMethod, methodSymbol, compilation, ct);
    }

    private static string? GetShapeRejectionReason(
        IMethodSymbol existingMethod,
        IMethodSymbol methodSymbol,
        CancellationToken ct)
    {
        if (!InheritedTypeAssemblyIdentityComparer.HasCompatibleReturnShape(existingMethod, methodSymbol, ct))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but an incompatible return type";
        }

        if (!HasSameParameterRefKinds(existingMethod, methodSymbol))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible parameter ref kinds";
        }

        if (!InheritedTypeAssemblyIdentityComparer.HaveSameParameterIdentities(existingMethod, methodSymbol, ct))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but parameters from different assemblies";
        }

        if (!HasSameParameterNames(existingMethod, methodSymbol))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible parameter names";
        }

        if (!HasSameParameterDefaultValues(existingMethod, methodSymbol))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible optional/default values";
        }

        if (!MethodSignatureFacts.HaveSameGenericConstraints(existingMethod, methodSymbol, ct))
        {
            return $"inherited generic method '{methodSymbol.Name}' has the same signature as another method but incompatible generic constraints";
        }

        return null;
    }

    private static string? GetContractRejectionReason(
        IMethodSymbol existingMethod,
        IMethodSymbol methodSymbol, Compilation compilation,
        CancellationToken ct)
    {
        if (!HasSameNullableAnnotations(existingMethod, methodSymbol, ct))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible nullable annotations";
        }

        if (!InheritedMethodFlowAttributeComparer.HasSameFlowAttributes(existingMethod, methodSymbol, ct))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible flow attributes";
        }

        if (!HasSameCallerInfoAttributes(existingMethod, methodSymbol, compilation, ct))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible caller info attributes";
        }

        if (!TupleElementNameComparer.HasSameElementNames(existingMethod, methodSymbol, ct))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible tuple element names";
        }

        if (!HasSameEffectiveWireName(existingMethod, methodSymbol))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but a different wire method name";
        }

        return null;
    }

    public static bool HasSameParameterRefKinds(IMethodSymbol left, IMethodSymbol right)
    {
        if (left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (left.Parameters[i].RefKind != right.Parameters[i].RefKind)
            {
                return false;
            }
        }

        return true;
    }

    public static bool HasSameParameterNames(IMethodSymbol left, IMethodSymbol right)
    {
        if (left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (left.Parameters[i].Name != right.Parameters[i].Name)
            {
                return false;
            }
        }

        return true;
    }

    public static bool HasSameParameterDefaultValues(IMethodSymbol left, IMethodSymbol right)
    {
        if (left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (!ParameterDefaultValueComparer.HasSameContract(
                left.Parameters[i],
                right.Parameters[i],
                DefaultLiteralOptions.SourceGenerator))
            {
                return false;
            }
        }

        return true;
    }

    public static bool HasSameNullableAnnotations(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken ct)
    {
        if (GetNullableTypeKey(left.ReturnType, left, ct) !=
            GetNullableTypeKey(right.ReturnType, right, ct) ||
            left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (GetNullableTypeKey(left.Parameters[i].Type, left, ct) !=
                GetNullableTypeKey(right.Parameters[i].Type, right, ct))
            {
                return false;
            }
        }

        return true;
    }

    public static bool HasSameCallerInfoAttributes(
        IMethodSymbol left,
        IMethodSymbol right, Compilation compilation,
        CancellationToken ct)
    {
        if (left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        var callerInfoSymbols = CallerInfoAttributeIdentity.Resolve(compilation);
        for (var i = 0; i < left.Parameters.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (CallerInfoAttributeIdentity.GetKey(left.Parameters[i], callerInfoSymbols, ct) !=
                CallerInfoAttributeIdentity.GetKey(right.Parameters[i], callerInfoSymbols, ct))
            {
                return false;
            }
        }

        return true;
    }

    public static string GetNullableTypeKey(
        ITypeSymbol type,
        IMethodSymbol method,
        CancellationToken ct) =>
        InheritedNullableTypeKey.Get(type, method, ct);

    public static bool HasSameEffectiveWireName(IMethodSymbol left, IMethodSymbol right) =>
        GetEffectiveWireName(left) == GetEffectiveWireName(right);

    public static MethodModel AddAdditionalExplicitImplementation(
        MethodModel method,
        INamedTypeSymbol implementationType)
    {
        var typeName = MethodModelFactory.GetExplicitImplementationType(implementationType);
        var types = new List<string>();
        foreach (var type in method.AdditionalExplicitImplementationTypes)
        {
            types.Add(type);
        }

        if (!types.Contains(typeName))
        {
            types.Add(typeName);
        }

        return method with
        {
            AdditionalExplicitImplementationTypes = types.ToEquatableArray(),
            RequiresDispatcherReceiverCast = true,
        };
    }

    private static string GetEffectiveWireName(IMethodSymbol methodSymbol) =>
        GetConfiguredMethodName(methodSymbol) ?? methodSymbol.Name;

    private static string? GetConfiguredMethodName(IMethodSymbol methodSymbol)
    {
        foreach (var attr in methodSymbol.GetAttributes())
        {
            if (!ServicesGeneratorTypeNames.IsRpcMethodAttribute(attr.AttributeClass))
            {
                continue;
            }

            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Name" && namedArg.Value.Value is string s)
                {
                    return s;
                }
            }
        }

        return null;
    }

}

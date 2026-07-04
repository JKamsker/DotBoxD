using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class InheritedMethodDeduplicator
{
    private const string DotBoxDMethodAttributeName = ServicesGeneratorTypeNames.DotBoxDMethodAttribute;

    public static string? GetConflictReason(
        IMethodSymbol existingMethod,
        IMethodSymbol methodSymbol,
        CancellationToken ct)
    {
        if (!HasCompatibleReturnShape(existingMethod, methodSymbol, ct))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but an incompatible return type";
        }

        if (!HasSameParameterRefKinds(existingMethod, methodSymbol))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible parameter ref kinds";
        }

        if (!MethodSignatureFacts.HaveSameGenericConstraints(existingMethod, methodSymbol, ct))
        {
            return $"inherited generic method '{methodSymbol.Name}' has the same signature as another method but incompatible generic constraints";
        }

        if (!HasSameNullableAnnotations(existingMethod, methodSymbol, ct))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible nullable annotations";
        }

        if (!HasSameFlowAttributes(existingMethod, methodSymbol, ct))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible flow attributes";
        }

        if (!TupleElementNameComparer.HasSameElementNames(existingMethod, methodSymbol, ct))
        {
            return $"inherited method '{methodSymbol.Name}' has the same signature as another method but incompatible tuple element names";
        }

        return HasSameEffectiveWireName(existingMethod, methodSymbol)
            ? null
            : $"inherited method '{methodSymbol.Name}' has the same signature as another method but a different wire method name";
    }

    public static bool HasCompatibleReturnShape(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken ct) =>
        left.RefKind == right.RefKind &&
        MethodSignatureFacts.GetCanonicalType(left.ReturnType, left, ct) ==
        MethodSignatureFacts.GetCanonicalType(right.ReturnType, right, ct);

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

    private static bool HasSameFlowAttributes(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken ct)
    {
        if (GetReturnFlowAttributeKey(left, ct) != GetReturnFlowAttributeKey(right, ct) ||
            left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (GetParameterFlowAttributeKey(left.Parameters[i], ct) !=
                GetParameterFlowAttributeKey(right.Parameters[i], ct))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetReturnFlowAttributeKey(IMethodSymbol method, CancellationToken ct) =>
        GetFlowAttributeKey(method.GetReturnTypeAttributes(), includeParameterOnlyAttributes: false, ct);

    private static string GetParameterFlowAttributeKey(IParameterSymbol parameter, CancellationToken ct) =>
        GetFlowAttributeKey(parameter.GetAttributes(), includeParameterOnlyAttributes: true, ct);

    private static string GetFlowAttributeKey(
        IEnumerable<AttributeData> attributes,
        bool includeParameterOnlyAttributes,
        CancellationToken ct)
    {
        var parts = new List<string>();
        foreach (var attr in attributes)
        {
            ct.ThrowIfCancellationRequested();
            var key = GetFlowAttributeKey(attr, includeParameterOnlyAttributes);
            if (key is not null)
            {
                parts.Add(key);
            }
        }

        parts.Sort(System.StringComparer.Ordinal);
        return string.Join(";", parts);
    }

    private static string? GetFlowAttributeKey(AttributeData attr, bool includeParameterOnlyAttributes)
    {
        var name = attr.AttributeClass?.ToDisplayString();
        return name switch
        {
            "System.Diagnostics.CodeAnalysis.MaybeNullAttribute" => name,
            "System.Diagnostics.CodeAnalysis.NotNullAttribute" => name,
            "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute" => StringAttributeKey(attr, name),
            "System.Diagnostics.CodeAnalysis.AllowNullAttribute" when includeParameterOnlyAttributes => name,
            "System.Diagnostics.CodeAnalysis.DisallowNullAttribute" when includeParameterOnlyAttributes => name,
            "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute" when includeParameterOnlyAttributes =>
                BooleanAttributeKey(attr, name),
            "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute" when includeParameterOnlyAttributes =>
                BooleanAttributeKey(attr, name),
            _ => null,
        };
    }

    private static string? BooleanAttributeKey(AttributeData attr, string name) =>
        attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is bool value
            ? name + "(" + (value ? "true" : "false") + ")"
            : null;

    private static string? StringAttributeKey(AttributeData attr, string name) =>
        attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string value
            ? name + "(\"" + value + "\")"
            : null;

    public static string GetNullableTypeKey(
        ITypeSymbol type,
        IMethodSymbol method,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is ITypeParameterSymbol typeParameter &&
            typeParameter.TypeParameterKind == TypeParameterKind.Method)
        {
            return "!!" + typeParameter.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                NullableSuffix(typeParameter.NullableAnnotation);
        }

        if (type.TypeKind == TypeKind.Dynamic)
        {
            return ServicesGeneratorTypeNames.GlobalObject + NullableSuffix(type.NullableAnnotation);
        }

        if (type is IArrayTypeSymbol array)
        {
            return GetNullableTypeKey(array.ElementType, method, ct) +
                "[" + new string(',', array.Rank - 1) + "]" +
                NullableSuffix(array.NullableAnnotation);
        }

        if (type is INamedTypeSymbol named)
        {
            return GetNullableNamedTypeKey(named, method, ct);
        }

        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            NullableSuffix(type.NullableAnnotation);
    }

    private static string GetNullableNamedTypeKey(
        INamedTypeSymbol type,
        IMethodSymbol method,
        CancellationToken ct)
    {
        var name = type.ContainingType is null
            ? GetNamespacePrefix(type) + type.MetadataName
            : GetNullableNamedTypeKey(type.ContainingType, method, ct) + "." + type.MetadataName;
        name += NullableSuffix(type.NullableAnnotation);
        if (type.TypeArguments.Length == 0)
        {
            return name;
        }

        var args = new List<string>();
        foreach (var arg in type.TypeArguments)
        {
            ct.ThrowIfCancellationRequested();
            args.Add(GetNullableTypeKey(arg, method, ct));
        }

        return name + "<" + string.Join(",", args) + ">";
    }

    private static string GetNamespacePrefix(INamedTypeSymbol type) =>
        type.ContainingNamespace.IsGlobalNamespace
            ? ServicesGeneratorTypeNames.GlobalPrefix
            : ServicesGeneratorTypeNames.GlobalPrefix + type.ContainingNamespace.ToDisplayString() + ".";

    private static string NullableSuffix(NullableAnnotation annotation) =>
        annotation == NullableAnnotation.Annotated ? "?" : string.Empty;

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
            if (attr.AttributeClass?.ToDisplayString() != DotBoxDMethodAttributeName)
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

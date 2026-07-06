using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class InheritedMethodFlowAttributeComparer
{
    public static bool HasSameFlowAttributes(
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
        GetFlowAttributeKey(method.GetReturnTypeAttributes(), method, includeParameterOnlyAttributes: false, ct);

    private static string GetParameterFlowAttributeKey(IParameterSymbol parameter, CancellationToken ct) =>
        GetFlowAttributeKey(
            parameter.GetAttributes(),
            parameter.ContainingSymbol as IMethodSymbol,
            includeParameterOnlyAttributes: true,
            ct);

    private static string GetFlowAttributeKey(
        IEnumerable<AttributeData> attributes,
        IMethodSymbol? containingMethod,
        bool includeParameterOnlyAttributes,
        CancellationToken ct)
    {
        var parts = new List<string>();
        foreach (var attr in attributes)
        {
            ct.ThrowIfCancellationRequested();
            var key = GetFlowAttributeKey(attr, containingMethod, includeParameterOnlyAttributes);
            if (key is not null)
            {
                parts.Add(key);
            }
        }

        parts.Sort(System.StringComparer.Ordinal);
        return string.Join(";", parts);
    }

    private static string? GetFlowAttributeKey(
        AttributeData attr,
        IMethodSymbol? containingMethod,
        bool includeParameterOnlyAttributes)
    {
        var name = attr.AttributeClass?.ToDisplayString();
        return name switch
        {
            "System.Diagnostics.CodeAnalysis.MaybeNullAttribute" => name,
            "System.Diagnostics.CodeAnalysis.NotNullAttribute" => name,
            "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute" =>
                ParameterReferenceAttributeKey(attr, name, containingMethod),
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

    private static string? ParameterReferenceAttributeKey(
        AttributeData attr,
        string name,
        IMethodSymbol? containingMethod)
    {
        if (attr.ConstructorArguments.Length != 1 || attr.ConstructorArguments[0].Value is not string value)
        {
            return null;
        }

        if (containingMethod is not null)
        {
            for (var i = 0; i < containingMethod.Parameters.Length; i++)
            {
                if (containingMethod.Parameters[i].Name == value)
                {
                    return name + "(#" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
                }
            }
        }

        return name + "(\"" + value + "\")";
    }
}

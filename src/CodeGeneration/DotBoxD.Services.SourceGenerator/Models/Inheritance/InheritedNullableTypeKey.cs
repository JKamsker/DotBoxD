using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class InheritedNullableTypeKey
{
    public static string Get(
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
            return Get(array.ElementType, method, ct) +
                "[" + new string(',', array.Rank - 1) + "]" +
                NullableSuffix(array.NullableAnnotation);
        }

        if (type is INamedTypeSymbol named)
        {
            return GetNamedTypeKey(named, method, ct);
        }

        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            NullableSuffix(type.NullableAnnotation);
    }

    private static string GetNamedTypeKey(
        INamedTypeSymbol type,
        IMethodSymbol method,
        CancellationToken ct)
    {
        var name = type.ContainingType is null
            ? GetNamespacePrefix(type) + type.MetadataName
            : GetNamedTypeKey(type.ContainingType, method, ct) + "." + type.MetadataName;
        name += NullableSuffix(type.NullableAnnotation);
        if (type.TypeArguments.Length == 0)
        {
            return name;
        }

        var args = new List<string>();
        foreach (var arg in type.TypeArguments)
        {
            ct.ThrowIfCancellationRequested();
            args.Add(Get(arg, method, ct));
        }

        return name + "<" + string.Join(",", args) + ">";
    }

    private static string GetNamespacePrefix(INamedTypeSymbol type) =>
        type.ContainingNamespace.IsGlobalNamespace
            ? ServicesGeneratorTypeNames.GlobalPrefix
            : ServicesGeneratorTypeNames.GlobalPrefix + type.ContainingNamespace.ToDisplayString() + ".";

    private static string NullableSuffix(NullableAnnotation annotation) =>
        annotation == NullableAnnotation.Annotated ? "?" : string.Empty;
}

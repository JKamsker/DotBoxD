using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class MethodSignatureFacts
{
    public static string GetSignatureKey(IMethodSymbol method, CancellationToken ct)
    {
        var parts = new List<string>();
        foreach (var parameter in method.Parameters)
        {
            ct.ThrowIfCancellationRequested();
            parts.Add(parameter.RefKind + ":" + GetCanonicalType(parameter.Type, method, ct));
        }

        return method.Name + "`" + method.Arity + "(" + string.Join(",", parts) + ")";
    }

    public static string GetSignatureKey(
        string methodName,
        int arity,
        EquatableArray<ParameterModel> parameters,
        CancellationToken ct)
    {
        var sb = new StringBuilder(IdentifierHelpers.UnescapeIdentifier(methodName));
        sb.Append('`').Append(arity).Append('(');
        for (var i = 0; i < parameters.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(parameters[i].RefKindKeyword).Append(parameters[i].SignatureType);
        }

        return sb.Append(')').ToString();
    }

    public static string GetCanonicalType(ITypeSymbol type, IMethodSymbol method, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (type is ITypeParameterSymbol typeParameter &&
            typeParameter.TypeParameterKind == TypeParameterKind.Method)
        {
            return "!!" + typeParameter.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (type is IArrayTypeSymbol array)
        {
            return GetCanonicalType(array.ElementType, method, ct) + "[" + new string(',', array.Rank - 1) + "]";
        }

        if (type is INamedTypeSymbol named)
        {
            if (named.IsTupleType)
            {
                var elements = new List<string>();
                foreach (var element in named.TupleElements)
                {
                    ct.ThrowIfCancellationRequested();
                    elements.Add(GetCanonicalType(element.Type, method, ct));
                }

                return "(" + string.Join(",", elements) + ")";
            }

            var name = GetMetadataName(named);
            if (!named.IsGenericType)
            {
                return name;
            }

            var args = new List<string>();
            foreach (var arg in named.TypeArguments)
            {
                ct.ThrowIfCancellationRequested();
                args.Add(GetCanonicalType(arg, method, ct));
            }

            return name + "<" + string.Join(",", args) + ">";
        }

        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    public static bool HaveSameGenericConstraints(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken ct)
    {
        if (left.Arity != right.Arity)
        {
            return false;
        }

        for (var i = 0; i < left.TypeParameters.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (!HaveSameConstraints(left.TypeParameters[i], left, right.TypeParameters[i], right, ct))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HaveSameConstraints(
        ITypeParameterSymbol left,
        IMethodSymbol leftMethod,
        ITypeParameterSymbol right,
        IMethodSymbol rightMethod,
        CancellationToken ct)
    {
        if (left.HasReferenceTypeConstraint != right.HasReferenceTypeConstraint ||
            left.ReferenceTypeConstraintNullableAnnotation != right.ReferenceTypeConstraintNullableAnnotation ||
            left.HasValueTypeConstraint != right.HasValueTypeConstraint ||
            left.HasUnmanagedTypeConstraint != right.HasUnmanagedTypeConstraint ||
            left.HasNotNullConstraint != right.HasNotNullConstraint ||
            left.HasConstructorConstraint != right.HasConstructorConstraint ||
            left.ConstraintTypes.Length != right.ConstraintTypes.Length)
        {
            return false;
        }

        for (var i = 0; i < left.ConstraintTypes.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (GetCanonicalType(left.ConstraintTypes[i], leftMethod, ct) !=
                GetCanonicalType(right.ConstraintTypes[i], rightMethod, ct))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        var names = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            names.Push(current.MetadataName);
        }

        var prefix = type.ContainingNamespace.IsGlobalNamespace
            ? "global::"
            : "global::" + type.ContainingNamespace.ToDisplayString() + ".";
        return prefix + string.Join(".", names);
    }
}

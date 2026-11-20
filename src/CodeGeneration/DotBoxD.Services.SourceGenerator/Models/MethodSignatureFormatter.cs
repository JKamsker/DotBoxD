using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class MethodSignatureFormatter
{
    // Compile against the repo's older Roslyn package, but preserve the C# 13 anti-constraint
    // when the generator is hosted by a newer compiler that exposes this symbol property.
    private static readonly PropertyInfo? s_allowsRefLikeTypeProperty =
        typeof(ITypeParameterSymbol).GetProperty("AllowsRefLikeType");

    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string GetTypeParameterList(IMethodSymbol method, CancellationToken ct)
    {
        if (!method.IsGenericMethod)
        {
            return string.Empty;
        }

        var names = new List<string>();
        foreach (var parameter in method.TypeParameters)
        {
            ct.ThrowIfCancellationRequested();
            names.Add(IdentifierHelpers.EscapeIdentifier(parameter.Name));
        }

        return "<" + string.Join(", ", names) + ">";
    }

    public static string GetConstraintClauses(
        IMethodSymbol method,
        ExternAliasTypeFormatter typeFormatter,
        CancellationToken ct)
    {
        if (!method.IsGenericMethod)
        {
            return string.Empty;
        }

        var clauses = new List<string>();
        foreach (var typeParameter in method.TypeParameters)
        {
            ct.ThrowIfCancellationRequested();
            var clause = GetConstraintClause(typeParameter, typeFormatter, ct);
            if (clause.Length > 0)
            {
                clauses.Add(clause);
            }
        }

        return string.Concat(clauses);
    }

    /// <summary>
    /// Explicit interface implementations inherit their constraints and may not restate them (CS0460).
    /// The only clauses C# accepts there are <c>class</c>, <c>struct</c>, and <c>default</c>, which are
    /// needed only so that a <c>T?</c> in the signature binds to the nullable-annotated type parameter
    /// instead of <see cref="System.Nullable{T}"/>. Value-type parameters already bind <c>T?</c> to
    /// <see cref="System.Nullable{T}"/>, so they never need a clause.
    /// </summary>
    public static string GetExplicitImplementationConstraintClauses(IMethodSymbol method, CancellationToken ct)
    {
        if (!method.IsGenericMethod)
        {
            return string.Empty;
        }

        var clauses = new List<string>();
        foreach (var typeParameter in method.TypeParameters)
        {
            ct.ThrowIfCancellationRequested();
            if (typeParameter.IsValueType || !SignatureAnnotatesTypeParameter(method, typeParameter, ct))
            {
                continue;
            }

            var constraint = typeParameter.IsReferenceType ? "class" : "default";
            clauses.Add($" where {IdentifierHelpers.EscapeIdentifier(typeParameter.Name)} : {constraint}");
        }

        return string.Concat(clauses);
    }

    private static bool SignatureAnnotatesTypeParameter(
        IMethodSymbol method,
        ITypeParameterSymbol typeParameter,
        CancellationToken ct)
    {
        if (AnnotatesTypeParameter(method.ReturnType, typeParameter, ct))
        {
            return true;
        }

        foreach (var parameter in method.Parameters)
        {
            if (AnnotatesTypeParameter(parameter.Type, typeParameter, ct))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnnotatesTypeParameter(ITypeSymbol type, ITypeParameterSymbol typeParameter, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return type switch
        {
            ITypeParameterSymbol candidate =>
                candidate.NullableAnnotation == NullableAnnotation.Annotated &&
                SymbolEqualityComparer.Default.Equals(candidate, typeParameter),
            IArrayTypeSymbol array => AnnotatesTypeParameter(array.ElementType, typeParameter, ct),
            IPointerTypeSymbol pointer => AnnotatesTypeParameter(pointer.PointedAtType, typeParameter, ct),
            IFunctionPointerTypeSymbol functionPointer =>
                SignatureAnnotatesTypeParameter(functionPointer.Signature, typeParameter, ct),
            INamedTypeSymbol named => NamedTypeAnnotatesTypeParameter(named, typeParameter, ct),
            _ => false,
        };
    }

    private static bool NamedTypeAnnotatesTypeParameter(
        INamedTypeSymbol named,
        ITypeParameterSymbol typeParameter,
        CancellationToken ct)
    {
        foreach (var typeArgument in named.TypeArguments)
        {
            if (AnnotatesTypeParameter(typeArgument, typeParameter, ct))
            {
                return true;
            }
        }

        return named.ContainingType is { } containing &&
            NamedTypeAnnotatesTypeParameter(containing, typeParameter, ct);
    }

    private static string GetConstraintClause(
        ITypeParameterSymbol typeParameter,
        ExternAliasTypeFormatter typeFormatter,
        CancellationToken ct)
    {
        var constraints = new List<string>();
        AddPrimaryConstraint(typeParameter, constraints);
        foreach (var constraintType in typeParameter.ConstraintTypes)
        {
            ct.ThrowIfCancellationRequested();
            constraints.Add(typeFormatter.Format(constraintType, s_qualifiedFormat));
        }

        if (typeParameter.HasConstructorConstraint)
        {
            constraints.Add("new()");
        }

        if (AllowsRefLikeType(typeParameter))
        {
            constraints.Add("allows ref struct");
        }

        return constraints.Count == 0
            ? string.Empty
            : $" where {IdentifierHelpers.EscapeIdentifier(typeParameter.Name)} : {string.Join(", ", constraints)}";
    }

    private static void AddPrimaryConstraint(ITypeParameterSymbol typeParameter, ICollection<string> constraints)
    {
        if (typeParameter.HasReferenceTypeConstraint)
        {
            constraints.Add(
                typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                    ? "class?"
                    : "class");
            return;
        }

        if (typeParameter.HasUnmanagedTypeConstraint)
        {
            constraints.Add("unmanaged");
            return;
        }

        if (typeParameter.HasValueTypeConstraint)
        {
            constraints.Add("struct");
            return;
        }

        if (typeParameter.HasNotNullConstraint)
        {
            constraints.Add("notnull");
        }
    }

    private static bool AllowsRefLikeType(ITypeParameterSymbol typeParameter) =>
        s_allowsRefLikeTypeProperty?.GetValue(typeParameter) is true;
}

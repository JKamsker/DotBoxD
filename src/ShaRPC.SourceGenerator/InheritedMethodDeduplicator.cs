using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class InheritedMethodDeduplicator
{
    private const string ShaRpcMethodAttributeName = "ShaRPC.Core.Attributes.ShaRpcMethodAttribute";

    public static bool HasCompatibleReturnShape(IMethodSymbol left, IMethodSymbol right) =>
        left.RefKind == right.RefKind &&
        SymbolEqualityComparer.Default.Equals(left.ReturnType, right.ReturnType);

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

    public static bool HasSameTupleElementNames(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken ct)
    {
        if (!HasSameTupleElementNames(left.ReturnType, right.ReturnType, ct) ||
            left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (!HasSameTupleElementNames(left.Parameters[i].Type, right.Parameters[i].Type, ct))
            {
                return false;
            }
        }

        return true;
    }

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
            return "global::System.Object" + NullableSuffix(type.NullableAnnotation);
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

    private static bool HasSameTupleElementNames(
        ITypeSymbol left,
        ITypeSymbol right,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (left is IArrayTypeSymbol leftArray && right is IArrayTypeSymbol rightArray)
        {
            return leftArray.Rank == rightArray.Rank &&
                HasSameTupleElementNames(leftArray.ElementType, rightArray.ElementType, ct);
        }

        if (left is INamedTypeSymbol leftNamed && right is INamedTypeSymbol rightNamed)
        {
            return HasSameTupleElementNames(leftNamed, rightNamed, ct);
        }

        return true;
    }

    private static bool HasSameTupleElementNames(
        INamedTypeSymbol left,
        INamedTypeSymbol right,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (left.IsTupleType || right.IsTupleType)
        {
            return left.IsTupleType &&
                right.IsTupleType &&
                HasSameTupleFields(left, right, ct);
        }

        if (left.TypeArguments.Length != right.TypeArguments.Length)
        {
            return false;
        }

        for (var i = 0; i < left.TypeArguments.Length; i++)
        {
            if (!HasSameTupleElementNames(left.TypeArguments[i], right.TypeArguments[i], ct))
            {
                return false;
            }
        }

        if (left.ContainingType is null || right.ContainingType is null)
        {
            return left.ContainingType is null && right.ContainingType is null;
        }

        return HasSameTupleElementNames(left.ContainingType, right.ContainingType, ct);
    }

    private static bool HasSameTupleFields(
        INamedTypeSymbol left,
        INamedTypeSymbol right,
        CancellationToken ct)
    {
        if (left.TupleElements.Length != right.TupleElements.Length)
        {
            return false;
        }

        for (var i = 0; i < left.TupleElements.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (left.TupleElements[i].Name != right.TupleElements[i].Name ||
                !HasSameTupleElementNames(left.TupleElements[i].Type, right.TupleElements[i].Type, ct))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetNamespacePrefix(INamedTypeSymbol type) =>
        type.ContainingNamespace.IsGlobalNamespace
            ? "global::"
            : "global::" + type.ContainingNamespace.ToDisplayString() + ".";

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
            if (attr.AttributeClass?.ToDisplayString() != ShaRpcMethodAttributeName)
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

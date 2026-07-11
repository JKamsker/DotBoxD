using Microsoft.CodeAnalysis;
using static DotBoxD.Plugins.Analyzer.Analysis.PluginServer.PluginServerFacadeNameFormatter;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerForwardedMethodSignature
{
    public static string Key(IMethodSymbol method)
        => method.Name + "(" + string.Join(
            ",",
            method.Parameters.Select(static parameter => TypeName(parameter.Type))) + ")";

    public static bool HasSameTupleElementNames(
        IMethodSymbol left,
        IMethodSymbol right,
        CancellationToken cancellationToken)
    {
        if (!HasSameTupleElementNames(left.ReturnType, right.ReturnType, cancellationToken) ||
            left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Parameters.Length; i++)
        {
            if (!HasSameTupleElementNames(left.Parameters[i].Type, right.Parameters[i].Type, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static string TypeName(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return TypeName(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
        }

        if (type is INamedTypeSymbol named)
        {
            if (named.IsTupleType && named.TupleUnderlyingType is { } tupleUnderlyingType)
            {
                return TypeName(tupleUnderlyingType);
            }

            if (named.TypeArguments.Length == 0)
            {
                return TypeIdentityName(named);
            }

            return TypeIdentityName(named.OriginalDefinition) + "<" +
                string.Join(",", named.TypeArguments.Select(static argument => TypeName(argument))) +
                ">";
        }

        return TypeIdentityName(type);
    }

    private static bool HasSameTupleElementNames(
        ITypeSymbol left,
        ITypeSymbol right,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (left is IArrayTypeSymbol leftArray && right is IArrayTypeSymbol rightArray)
        {
            return leftArray.Rank == rightArray.Rank &&
                HasSameTupleElementNames(leftArray.ElementType, rightArray.ElementType, cancellationToken);
        }

        return left is INamedTypeSymbol leftNamed && right is INamedTypeSymbol rightNamed
            ? HasSameTupleElementNames(leftNamed, rightNamed, cancellationToken)
            : true;
    }

    private static bool HasSameTupleElementNames(
        INamedTypeSymbol left,
        INamedTypeSymbol right,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsTupleCompatible(left) || IsTupleCompatible(right))
        {
            return HasSameTupleFields(left, right, cancellationToken);
        }

        if (left.TypeArguments.Length != right.TypeArguments.Length)
        {
            return false;
        }

        for (var i = 0; i < left.TypeArguments.Length; i++)
        {
            if (!HasSameTupleElementNames(left.TypeArguments[i], right.TypeArguments[i], cancellationToken))
            {
                return false;
            }
        }

        return left.ContainingType is null || right.ContainingType is null
            ? left.ContainingType is null && right.ContainingType is null
            : HasSameTupleElementNames(left.ContainingType, right.ContainingType, cancellationToken);
    }

    private static bool HasSameTupleFields(
        INamedTypeSymbol left,
        INamedTypeSymbol right,
        CancellationToken cancellationToken)
    {
        var leftElements = TupleElements(left, cancellationToken);
        var rightElements = TupleElements(right, cancellationToken);
        if (leftElements.Count != rightElements.Count)
        {
            return false;
        }

        for (var i = 0; i < leftElements.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (leftElements[i].Name != rightElements[i].Name ||
                !HasSameTupleElementNames(leftElements[i].Type, rightElements[i].Type, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static List<TupleElementInfo> TupleElements(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var elements = new List<TupleElementInfo>();
        AddTupleElements(type, elements, cancellationToken);
        return elements;
    }

    private static void AddTupleElements(
        INamedTypeSymbol type,
        List<TupleElementInfo> elements,
        CancellationToken cancellationToken)
    {
        if (type.IsTupleType)
        {
            for (var i = 0; i < type.TupleElements.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var element = type.TupleElements[i];
                elements.Add(new TupleElementInfo(ExplicitTupleElementName(element, i), element.Type));
            }

            return;
        }

        for (var i = 0; i < type.TypeArguments.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var argument = type.TypeArguments[i];
            if (i == 7 && type.TypeArguments.Length == 8 && argument is INamedTypeSymbol rest && IsValueTuple(rest))
            {
                AddTupleElements(rest, elements, cancellationToken);
                continue;
            }

            elements.Add(new TupleElementInfo(string.Empty, argument));
        }
    }

    private static bool IsTupleCompatible(INamedTypeSymbol type)
        => type.IsTupleType || IsValueTuple(type);

    private static bool IsValueTuple(INamedTypeSymbol type)
        => type.ContainingNamespace.ToDisplayString() == "System" &&
           type.Name == "ValueTuple" &&
           type.TypeArguments.Length is >= 1 and <= 8;

    private static string ExplicitTupleElementName(IFieldSymbol element, int index)
    {
        var defaultName = "Item" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return element.Name == defaultName ? string.Empty : element.Name;
    }

    private readonly record struct TupleElementInfo(string Name, ITypeSymbol Type);
}

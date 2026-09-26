using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Translation;

internal static class ContainsMethodFilterTranslator
{
    private static readonly HashSet<Type> SupportedInstanceContainsDefinitions =
    [
        typeof(Collection<>),
        typeof(Dictionary<,>.KeyCollection),
        typeof(HashSet<>),
        typeof(LinkedList<>),
        typeof(List<>),
        typeof(Queue<>),
        typeof(ReadOnlyCollection<>),
        typeof(ReadOnlyDictionary<,>.KeyCollection),
        typeof(SortedDictionary<,>.KeyCollection),
        typeof(SortedSet<>),
        typeof(Stack<>)
    ];

    private static readonly HashSet<Type> SupportedCollectionInterfaceDefinitions =
    [
        typeof(ICollection<>),
        typeof(IReadOnlySet<>),
        typeof(ISet<>)
    ];

    public static bool TryTranslate(
        MethodCallExpression call,
        ParameterExpression parameter,
        out QueryFilter filter)
    {
        filter = QueryFilter.MatchAll;
        if (call.Method.Name != nameof(Enumerable.Contains))
        {
            return false;
        }

        if (!TryReadContainsOperands(call, out var collection, out var item) ||
            !MemberPathReader.TryReadPath(item, parameter, out var path))
        {
            return false;
        }

        ValidateSupportedContainsMethod(call);
        // Spans need special capture because they cannot be boxed. Other collection
        // conversions must run before comparer validation and value capture.
        var unwrapped = call.Method.DeclaringType == typeof(MemoryExtensions)
            ? PrepareSpanCollection(collection)
            : collection;
        var capturedCollection = QueryValueFactory.EvaluateCollection(unwrapped, parameter);
        if (call.Method.DeclaringType == typeof(Enumerable))
        {
            filter = QueryFilter.In(path, LinqContainsCapture.Capture(capturedCollection, call, unwrapped));
            return true;
        }

        CollectionContainsSupport.Validate(call, capturedCollection);
        RejectUnsupportedContainsComparer(call, capturedCollection);
        filter = QueryFilter.In(path, QueryValueFactory.ToValues(capturedCollection, unwrapped));
        return true;
    }

    private static bool TryReadContainsOperands(
        MethodCallExpression call,
        out Expression collection,
        out Expression item)
    {
        collection = null!;
        item = null!;
        if (call.Object is null)
        {
            return TryReadStaticContainsOperands(call, out collection, out item);
        }

        if (call.Arguments.Count != 1)
        {
            return false;
        }

        collection = call.Object;
        item = call.Arguments[0];
        return true;
    }

    private static bool TryReadStaticContainsOperands(
        MethodCallExpression call,
        out Expression collection,
        out Expression item)
    {
        collection = null!;
        item = null!;
        if (call.Arguments.Count != 2)
        {
            return false;
        }

        collection = call.Arguments[0];
        item = call.Arguments[1];
        return true;
    }

    private static void ValidateSupportedContainsMethod(MethodCallExpression call)
    {
        if (call.Object is not null)
        {
            ValidateSupportedInstanceContains(call);
            return;
        }

        ValidateSupportedStaticContains(call);
    }

    private static void ValidateSupportedInstanceContains(MethodCallExpression call)
    {
        if (!IsSupportedInstanceContains(call.Method))
        {
            throw QueryTranslationException.Unsupported(
                call,
                "custom instance Contains methods are not supported; use ToArray() when enumeration membership semantics are intended.");
        }
    }

    private static void ValidateSupportedStaticContains(MethodCallExpression call)
    {
        if (!IsSupportedStaticContains(call.Method))
        {
            throw QueryTranslationException.Unsupported(
                call,
                "custom static Contains methods are not supported; use ToArray() when enumeration membership semantics are intended.");
        }
    }

    internal static void RejectUnsupportedContainsComparer(
        MethodCallExpression call,
        object collection)
    {
        // HashSet/Dictionary-style collections can carry a custom equality comparer that changes membership
        // semantics even when written as static Enumerable.Contains(source, item).
        if (HasUnsupportedComparer(call, collection))
        {
            throw QueryTranslationException.Unsupported(
                call,
                "Contains over a collection with a custom, case-insensitive, or culture-sensitive comparer is not supported; use a default/ordinal collection or ToArray() for enumeration membership.");
        }
    }

    private static bool HasUnsupportedComparer(MethodCallExpression call, object collection)
    {
        try
        {
            return CollectionComparerSupport.HasUnsupportedComparer(collection);
        }
        catch (Exception ex) when (ex is not QueryTranslationException and not OperationCanceledException)
        {
            throw new QueryTranslationException(
                $"Unsupported query expression '{call}' (node '{call.NodeType}'). Contains comparer probing failed; use a default/ordinal collection.",
                ex);
        }
    }

    private static bool IsSupportedStaticContains(MethodInfo method) =>
        method.DeclaringType == typeof(Enumerable) ||
        method.DeclaringType == typeof(MemoryExtensions);

    private static bool IsSupportedInstanceContains(MethodInfo method)
    {
        var declaringType = method.DeclaringType;
        if (declaringType is null)
        {
            return false;
        }

        if (declaringType.IsInterface)
        {
            return IsSupportedCollectionInterface(declaringType);
        }

        if (!declaringType.IsGenericType)
        {
            return false;
        }

        var definition = declaringType.GetGenericTypeDefinition();
        return SupportedInstanceContainsDefinitions.Contains(definition);
    }

    private static bool IsSupportedCollectionInterface(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return SupportedCollectionInterfaceDefinitions.Contains(definition);
    }

    // Framework array-to-span conversions preserve the entire array. Other span expressions
    // must run before copying their selected values into a boxable array.
    private static Expression PrepareSpanCollection(Expression collection)
    {
        MethodInfo? method = null;
        Expression? operand = null;
        if (collection is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            method = unary.Method;
            operand = unary.Operand;
        }
        else if (collection is MethodCallExpression { Arguments.Count: 1 } call)
        {
            method = call.Method;
            operand = call.Arguments[0];
        }

        if (operand?.Type.IsArray == true && method is { Name: "op_Implicit" } && IsSpanType(method.DeclaringType))
        {
            return operand;
        }

        return IsSpanType(collection.Type)
            ? Expression.Call(collection, nameof(ReadOnlySpan<int>.ToArray), Type.EmptyTypes)
            : collection;
    }

    private static bool IsSpanType(Type? type)
    {
        if (type is not { IsGenericType: true })
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(Span<>) || definition == typeof(ReadOnlySpan<>);
    }
}

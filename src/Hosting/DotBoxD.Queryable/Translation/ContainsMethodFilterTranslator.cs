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
        var unwrapped = UnwrapSpan(collection);
        RejectUnsupportedContainsComparer(call, unwrapped, parameter);
        filter = QueryFilter.In(path, QueryValueFactory.ToValues(unwrapped, parameter));
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
                "custom instance Contains methods are not supported; use Enumerable.Contains(collection, member) when enumeration membership semantics are intended.");
        }
    }

    private static void ValidateSupportedStaticContains(MethodCallExpression call)
    {
        if (!IsSupportedStaticContains(call.Method))
        {
            throw QueryTranslationException.Unsupported(
                call,
                "custom static Contains methods are not supported; use Enumerable.Contains(collection, member) when enumeration membership semantics are intended.");
        }
    }

    private static void RejectUnsupportedContainsComparer(
        MethodCallExpression call,
        Expression collection,
        ParameterExpression parameter)
    {
        // HashSet/Dictionary-style collections can carry a custom equality comparer that changes membership
        // semantics even when written as static Enumerable.Contains(source, item).
        if (QueryValueFactory.TryEvaluateObject(collection, parameter, out var collectionObject) &&
            collectionObject is not null &&
            HasUnsupportedComparer(call, collectionObject))
        {
            throw QueryTranslationException.Unsupported(
                call,
                "Contains over a collection with a custom, case-insensitive, or culture-sensitive comparer is not supported; use a default/ordinal collection.");
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
        string.Equals(method.DeclaringType?.FullName, "System.MemoryExtensions", StringComparison.Ordinal);

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

    // `array.Contains(x)` binds to MemoryExtensions.Contains(ReadOnlySpan<T>, T); the source then appears as
    // an implicit T[] -> ReadOnlySpan<T> conversion wrapping the real collection.
    private static Expression UnwrapSpan(Expression collection)
    {
        var stripped = MemberPathReader.StripConvert(collection);
        if (stripped is MethodCallExpression { Method.Name: "op_Implicit" } conversion)
        {
            var operand = conversion.Object ?? (conversion.Arguments.Count == 1 ? conversion.Arguments[0] : null);
            if (operand is not null)
            {
                return operand;
            }
        }

        return stripped;
    }
}

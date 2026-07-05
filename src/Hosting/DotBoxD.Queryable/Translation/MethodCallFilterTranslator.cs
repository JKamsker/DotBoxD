using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Translation;

/// <summary>
/// Translates the supported method-call predicates: ordinal string <c>Contains</c>/<c>StartsWith</c>/
/// <c>EndsWith</c>/<c>Equals</c> (with an optional <see cref="StringComparison"/> selecting case
/// sensitivity) against a constant, and <c>Contains</c> over a constant collection (lowered to
/// <see cref="QueryFilterKind.In"/>). The <c>makeValue</c> callback assigns capture ordinals.
/// </summary>
internal static class MethodCallFilterTranslator
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

    private static readonly Dictionary<string, QueryComparisonOperator> StringOperators = new(StringComparer.Ordinal)
    {
        [nameof(string.Contains)] = QueryComparisonOperator.StringContains,
        [nameof(string.StartsWith)] = QueryComparisonOperator.StringStartsWith,
        [nameof(string.EndsWith)] = QueryComparisonOperator.StringEndsWith,
        [nameof(string.Equals)] = QueryComparisonOperator.Equal
    };

    public static QueryFilter Translate(
        MethodCallExpression call,
        ParameterExpression parameter,
        Func<object?, Expression, QueryValue> makeValue)
    {
        if (TryTranslateString(call, parameter, makeValue, out var stringFilter))
        {
            return stringFilter;
        }

        if (TryTranslateContains(call, parameter, out var inFilter))
        {
            return inFilter;
        }

        throw QueryTranslationException.Unsupported(
            call,
            "supported calls are string Contains/StartsWith/EndsWith/Equals and Contains over a constant collection.");
    }

    private static bool TryTranslateString(
        MethodCallExpression call,
        ParameterExpression parameter,
        Func<object?, Expression, QueryValue> makeValue,
        out QueryFilter filter)
    {
        filter = QueryFilter.MatchAll;
        if (call.Method.DeclaringType != typeof(string))
        {
            return false;
        }

        if (call.Object is null)
        {
            return TryTranslateStaticStringEquals(call, parameter, makeValue, out filter);
        }

        if (!TryReadInstanceStringCall(call, parameter, out var path, out var raw) ||
            !StringOperators.TryGetValue(call.Method.Name, out var op))
        {
            return false;
        }

        var comparisonArgument = call.Arguments.Count == 2 ? call.Arguments[1] : null;
        var ignoreCase = ReadIgnoreCase(call, op, comparisonArgument, parameter);
        filter = QueryFilter.Compare(path, op, makeValue(raw, call.Arguments[0]), ignoreCase);
        return true;
    }

    private static bool TryReadInstanceStringCall(
        MethodCallExpression call,
        ParameterExpression parameter,
        out string path,
        out object? raw)
    {
        path = "";
        raw = null;
        return MemberPathReader.TryReadPath(call.Object!, parameter, out path) &&
               call.Arguments.Count is 1 or 2 &&
               QueryValueFactory.TryEvaluateObject(call.Arguments[0], parameter, out raw) &&
               raw is string;
    }

    private static bool TryTranslateStaticStringEquals(
        MethodCallExpression call,
        ParameterExpression parameter,
        Func<object?, Expression, QueryValue> makeValue,
        out QueryFilter filter)
    {
        filter = QueryFilter.MatchAll;
        if (call.Method.Name != nameof(string.Equals) ||
            call.Arguments.Count is not 2 and not 3 ||
            !TryReadStringEqualsOperands(
                call.Arguments[0],
                call.Arguments[1],
                parameter,
                out var path,
                out var valueExpression,
                out var raw) ||
            raw is not string)
        {
            return false;
        }

        var comparisonArgument = call.Arguments.Count == 3 ? call.Arguments[2] : null;
        var ignoreCase = ReadIgnoreCase(call, QueryComparisonOperator.Equal, comparisonArgument, parameter);
        filter = QueryFilter.Compare(
            path,
            QueryComparisonOperator.Equal,
            makeValue(raw, valueExpression),
            ignoreCase);
        return true;
    }

    private static bool TryReadStringEqualsOperands(
        Expression left,
        Expression right,
        ParameterExpression parameter,
        out string path,
        out Expression valueExpression,
        out object? raw)
    {
        if (MemberPathReader.TryReadPath(left, parameter, out path) &&
            QueryValueFactory.TryEvaluateObject(right, parameter, out raw))
        {
            valueExpression = right;
            return true;
        }

        if (MemberPathReader.TryReadPath(right, parameter, out path) &&
            QueryValueFactory.TryEvaluateObject(left, parameter, out raw))
        {
            valueExpression = left;
            return true;
        }

        path = "";
        valueExpression = left;
        raw = null;
        return false;
    }

    private static bool TryTranslateContains(
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
        // semantics even when written as static Enumerable.Contains(source, item); lowering that to a plain
        // In would silently drop or add matches. Reject custom/culture-sensitive comparers rather than
        // mis-translate.
        if (QueryValueFactory.TryEvaluateObject(collection, parameter, out var collectionObject) &&
            collectionObject is not null &&
            CollectionComparerSupport.HasUnsupportedComparer(collectionObject))
        {
            throw QueryTranslationException.Unsupported(
                call,
                "Contains over a collection with a custom, case-insensitive, or culture-sensitive comparer is not supported; use a default/ordinal collection.");
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
    // an implicit T[] -> ReadOnlySpan<T> conversion (an op_Implicit call or Convert) wrapping the real
    // collection. Unwrap it so the underlying array/collection can be evaluated as a constant.
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

    private static bool ReadIgnoreCase(
        MethodCallExpression call,
        QueryComparisonOperator op,
        Expression? comparisonArgument,
        ParameterExpression parameter)
    {
        if (comparisonArgument is null)
        {
            if (op is QueryComparisonOperator.StringStartsWith or QueryComparisonOperator.StringEndsWith)
            {
                throw QueryTranslationException.Unsupported(
                    call,
                    $"string {call.Method.Name} one-argument overload is culture-sensitive; pass StringComparison.Ordinal or OrdinalIgnoreCase.");
            }

            return false;
        }

        if (QueryValueFactory.TryEvaluateObject(comparisonArgument, parameter, out var raw) &&
            raw is StringComparison comparison)
        {
            // The evaluator compares ordinally, so only the ordinal modes can be honored faithfully. A
            // culture-sensitive overload would silently change semantics — reject it instead of downgrading.
            return comparison switch
            {
                StringComparison.Ordinal => false,
                StringComparison.OrdinalIgnoreCase => true,
                _ => throw QueryTranslationException.Unsupported(
                    call,
                    $"StringComparison.{comparison} is culture-sensitive; only Ordinal and OrdinalIgnoreCase are supported."),
            };
        }

        throw QueryTranslationException.Unsupported(
            call,
            "string filters support only ordinal overloads: one-argument Contains/Equals, or StringComparison.Ordinal/OrdinalIgnoreCase.");
    }

}

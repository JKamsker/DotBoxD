using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Translation;

internal static class StringMethodFilterTranslator
{
    private static readonly Dictionary<string, QueryComparisonOperator> StringOperators = new(StringComparer.Ordinal)
    {
        [nameof(string.Contains)] = QueryComparisonOperator.StringContains,
        [nameof(string.StartsWith)] = QueryComparisonOperator.StringStartsWith,
        [nameof(string.EndsWith)] = QueryComparisonOperator.StringEndsWith,
        [nameof(string.Equals)] = QueryComparisonOperator.Equal
    };

    public static bool TryTranslate(
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
            // The evaluator compares ordinally, so only ordinal modes can be honored faithfully.
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

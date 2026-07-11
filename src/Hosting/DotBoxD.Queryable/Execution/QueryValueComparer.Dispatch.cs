using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Execution;

public static partial class QueryValueComparer
{
    private static readonly Dictionary<QueryComparisonOperator, ComparisonEvaluator> ComparisonEvaluators = new()
    {
        [QueryComparisonOperator.Equal] = CompareEqual,
        [QueryComparisonOperator.NotEqual] = CompareNotEqual,
        [QueryComparisonOperator.GreaterThan] = CompareGreaterThan,
        [QueryComparisonOperator.GreaterThanOrEqual] = CompareGreaterThanOrEqual,
        [QueryComparisonOperator.LessThan] = CompareLessThan,
        [QueryComparisonOperator.LessThanOrEqual] = CompareLessThanOrEqual,
        [QueryComparisonOperator.StringContains] = static (actual, expected, ignoreCase) =>
            StringMatch(actual, expected, ignoreCase, MatchMode.Contains),
        [QueryComparisonOperator.StringStartsWith] = static (actual, expected, ignoreCase) =>
            StringMatch(actual, expected, ignoreCase, MatchMode.StartsWith),
        [QueryComparisonOperator.StringEndsWith] = static (actual, expected, ignoreCase) =>
            StringMatch(actual, expected, ignoreCase, MatchMode.EndsWith),
    };

    private static readonly Dictionary<QueryValueKind, EqualityEvaluator> EqualityEvaluators = new()
    {
        [QueryValueKind.Null] = EqualNull,
        [QueryValueKind.Boolean] = EqualBoolean,
        [QueryValueKind.String] = EqualString,
        [QueryValueKind.Integer] = EqualInteger,
        [QueryValueKind.UnsignedInteger] = EqualUnsignedInteger,
        [QueryValueKind.Decimal] = EqualDecimal,
        [QueryValueKind.Number] = EqualNumber,
        [QueryValueKind.Guid] = EqualGuid,
        [QueryValueKind.Timestamp] = EqualTimestamp,
    };

    private static readonly Dictionary<QueryValueKind, OrderEvaluator> OrderEvaluators = new()
    {
        [QueryValueKind.Integer] = static (actual, expected, _) =>
            OrderedNumeric(actual, (decimal)expected.Integer, () => (double)expected.Integer),
        [QueryValueKind.UnsignedInteger] = static (actual, expected, _) =>
            OrderedNumeric(actual, (decimal)expected.UnsignedInteger, () => (double)expected.UnsignedInteger),
        [QueryValueKind.Decimal] = static (actual, expected, _) =>
            OrderedNumeric(actual, expected.Decimal, () => (double)expected.Decimal),
        [QueryValueKind.Number] = OrderNumber,
        [QueryValueKind.Timestamp] = OrderTimestamp,
        [QueryValueKind.String] = OrderString,
    };

    private delegate bool ComparisonEvaluator(object? actual, QueryValue expected, bool ignoreCase);

    private delegate bool EqualityEvaluator(
        object actual,
        QueryValue expected,
        bool ignoreCase,
        out bool equal);

    private delegate int? OrderEvaluator(object? actual, QueryValue expected, bool ignoreCase);

    private static bool CompareEqual(object? actual, QueryValue expected, bool ignoreCase)
        => AreEqual(actual, expected, ignoreCase);

    private static bool CompareNotEqual(object? actual, QueryValue expected, bool ignoreCase)
        => TryAreEqual(actual, expected, ignoreCase, out var equal) && !equal;

    private static bool CompareGreaterThan(object? actual, QueryValue expected, bool ignoreCase)
        => Ordered(actual, expected, ignoreCase) is { } c && c > 0;

    private static bool CompareGreaterThanOrEqual(object? actual, QueryValue expected, bool ignoreCase)
        => Ordered(actual, expected, ignoreCase) is { } c && c >= 0;

    private static bool CompareLessThan(object? actual, QueryValue expected, bool ignoreCase)
        => Ordered(actual, expected, ignoreCase) is { } c && c < 0;

    private static bool CompareLessThanOrEqual(object? actual, QueryValue expected, bool ignoreCase)
        => Ordered(actual, expected, ignoreCase) is { } c && c <= 0;

    private static bool EqualNull(object actual, QueryValue expected, bool ignoreCase, out bool equal)
    {
        equal = false;
        return true;
    }

    private static bool EqualBoolean(object actual, QueryValue expected, bool ignoreCase, out bool equal)
    {
        if (actual is bool value)
        {
            equal = value == expected.Boolean;
            return true;
        }

        equal = false;
        return false;
    }

    private static bool EqualString(object actual, QueryValue expected, bool ignoreCase, out bool equal)
    {
        if (actual is string value)
        {
            equal = string.Equals(value, expected.String, StringComparisonMode(ignoreCase));
            return true;
        }

        equal = false;
        return false;
    }

    private static bool EqualInteger(object actual, QueryValue expected, bool ignoreCase, out bool equal)
        => TryAreNumericEqual(actual, (decimal)expected.Integer, () => (double)expected.Integer, out equal);

    private static bool EqualUnsignedInteger(object actual, QueryValue expected, bool ignoreCase, out bool equal)
        => TryAreNumericEqual(actual, (decimal)expected.UnsignedInteger, () => (double)expected.UnsignedInteger, out equal);

    private static bool EqualDecimal(object actual, QueryValue expected, bool ignoreCase, out bool equal)
        => TryAreNumericEqual(actual, expected.Decimal, () => (double)expected.Decimal, out equal);

    private static bool EqualNumber(object actual, QueryValue expected, bool ignoreCase, out bool equal)
    {
        if (TryToDouble(actual, out var number))
        {
            equal = number.Equals(expected.Number);
            return true;
        }

        equal = false;
        return false;
    }

    private static bool EqualGuid(object actual, QueryValue expected, bool ignoreCase, out bool equal)
    {
        if (actual is Guid value)
        {
            equal = value == expected.Guid;
            return true;
        }

        equal = false;
        return false;
    }

    private static bool EqualTimestamp(object actual, QueryValue expected, bool ignoreCase, out bool equal)
    {
        if (TryToInstantTicks(actual, out var ticks))
        {
            equal = ticks == expected.Timestamp.UtcTicks;
            return true;
        }

        equal = false;
        return false;
    }

    private static int? OrderNumber(object? actual, QueryValue expected, bool ignoreCase)
        => TryToDouble(actual, out var number) ? number.CompareTo(expected.Number) : null;

    private static int? OrderTimestamp(object? actual, QueryValue expected, bool ignoreCase)
        => TryToInstantTicks(actual, out var ticks) ? ticks.CompareTo(expected.Timestamp.UtcTicks) : null;

    private static int? OrderString(object? actual, QueryValue expected, bool ignoreCase)
        => actual is string value
            ? string.Compare(value, expected.String, StringComparisonMode(ignoreCase))
            : null;

    private static StringComparison StringComparisonMode(bool ignoreCase)
        => ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

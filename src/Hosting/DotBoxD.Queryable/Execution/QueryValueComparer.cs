using System.Globalization;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Execution;

/// <summary>
/// Compares a runtime member value against a portable <see cref="QueryValue"/> for a given
/// <see cref="QueryComparisonOperator"/>. Integral values are compared exactly (a <see cref="QueryValueKind.Integer"/>
/// literal against an integral member never loses precision through a <see cref="double"/> round-trip), integral
/// and floating values still interoperate by widening to <see cref="double"/>, enums compare by their underlying
/// value, strings ordinally (optionally ignoring case, including for ordered comparisons), and booleans by value.
/// Incomparable operand pairs evaluate to <see langword="false"/> rather than throwing — and an incomparable
/// pair is also <see langword="false"/> under <see cref="QueryComparisonOperator.NotEqual"/>, never a match.
/// </summary>
public static class QueryValueComparer
{
    /// <summary>Evaluates <c>actual op expected</c>.</summary>
    public static bool Compare(object? actual, QueryComparisonOperator op, QueryValue expected, bool ignoreCase) => op switch
    {
        QueryComparisonOperator.Equal => AreEqual(actual, expected, ignoreCase),
        QueryComparisonOperator.NotEqual => TryAreEqual(actual, expected, ignoreCase, out var equal) && !equal,
        QueryComparisonOperator.GreaterThan => Ordered(actual, expected, ignoreCase) is { } c && c > 0,
        QueryComparisonOperator.GreaterThanOrEqual => Ordered(actual, expected, ignoreCase) is { } c && c >= 0,
        QueryComparisonOperator.LessThan => Ordered(actual, expected, ignoreCase) is { } c && c < 0,
        QueryComparisonOperator.LessThanOrEqual => Ordered(actual, expected, ignoreCase) is { } c && c <= 0,
        QueryComparisonOperator.StringContains => StringMatch(actual, expected, ignoreCase, MatchMode.Contains),
        QueryComparisonOperator.StringStartsWith => StringMatch(actual, expected, ignoreCase, MatchMode.StartsWith),
        QueryComparisonOperator.StringEndsWith => StringMatch(actual, expected, ignoreCase, MatchMode.EndsWith),
        _ => false,
    };

    /// <summary>Returns <see langword="true"/> when <paramref name="actual"/> equals any of <paramref name="candidates"/>.</summary>
    public static bool IsAnyEqual(object? actual, IReadOnlyList<QueryValue> candidates, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        for (var i = 0; i < candidates.Count; i++)
        {
            if (AreEqual(actual, candidates[i], ignoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines value equality between a runtime value and a portable value.</summary>
    public static bool AreEqual(object? actual, QueryValue expected, bool ignoreCase)
        => TryAreEqual(actual, expected, ignoreCase, out var equal) && equal;

    /// <summary>
    /// Computes equality and reports whether the operands were comparable at all. Returns
    /// <see langword="false"/> for an incomparable pair (e.g. a numeric literal against a string member) with
    /// <paramref name="equal"/> set to <see langword="false"/>, so callers can keep <c>!=</c> false for
    /// incomparable operands instead of conflating "not equal" with "incomparable". A <see langword="null"/>
    /// runtime value is always comparable and equals only the null literal.
    /// </summary>
    private static bool TryAreEqual(object? actual, QueryValue expected, bool ignoreCase, out bool equal)
    {
        if (actual is null)
        {
            equal = expected.Kind == QueryValueKind.Null;
            return true;
        }

        switch (expected.Kind)
        {
            case QueryValueKind.Null:
                equal = false;
                return true;
            case QueryValueKind.Boolean:
                if (actual is bool b)
                {
                    equal = b == expected.Boolean;
                    return true;
                }

                break;
            case QueryValueKind.String:
                if (actual is string s)
                {
                    equal = string.Equals(s, expected.String, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                    return true;
                }

                break;
            case QueryValueKind.Integer:
                // Exact integral comparison when the runtime value is genuinely integral; otherwise widen to
                // double so an integral literal still interoperates with a floating-point member.
                if (TryToInt64(actual, out var i))
                {
                    equal = i == expected.Integer;
                    return true;
                }

                if (TryToDouble(actual, out var di))
                {
                    equal = di.Equals((double)expected.Integer);
                    return true;
                }

                break;
            case QueryValueKind.Number:
                if (TryToDouble(actual, out var dn))
                {
                    equal = dn.Equals(expected.Number);
                    return true;
                }

                break;
        }

        equal = false;
        return false;
    }

    private static int? Ordered(object? actual, QueryValue expected, bool ignoreCase)
    {
        switch (expected.Kind)
        {
            case QueryValueKind.Integer:
                if (TryToInt64(actual, out var i))
                {
                    return i.CompareTo(expected.Integer);
                }

                return TryToDouble(actual, out var di) ? di.CompareTo((double)expected.Integer) : null;
            case QueryValueKind.Number:
                return TryToDouble(actual, out var dn) ? dn.CompareTo(expected.Number) : null;
            case QueryValueKind.String:
                return actual is string s
                    ? string.Compare(s, expected.String, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    : null;
            default:
                return null;
        }
    }

    private static bool StringMatch(object? actual, QueryValue expected, bool ignoreCase, MatchMode mode)
    {
        if (actual is not string s || expected.String is not { } needle)
        {
            return false;
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return mode switch
        {
            MatchMode.Contains => s.Contains(needle, comparison),
            MatchMode.StartsWith => s.StartsWith(needle, comparison),
            MatchMode.EndsWith => s.EndsWith(needle, comparison),
            _ => false,
        };
    }

    // Exact Int64 view of a genuinely integral runtime value (integer types, in-range ulong, non-ulong enums).
    // Floating-point and over-range/ulong-backed values return false so callers fall back to a double compare.
    private static bool TryToInt64(object? value, out long result)
    {
        switch (value)
        {
            case sbyte or byte or short or ushort or int or uint or long:
                result = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            case ulong u when u <= long.MaxValue:
                result = (long)u;
                return true;
            case Enum e when Enum.GetUnderlyingType(e.GetType()) != typeof(ulong):
                result = Convert.ToInt64(e, CultureInfo.InvariantCulture);
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryToDouble(object? value, out double result)
    {
        switch (value)
        {
            case null:
            case bool:
            case string:
                result = 0;
                return false;
            case Enum e:
                // Convert.ToDouble handles every underlying type (incl. ulong > long.MaxValue) without overflow.
                result = Convert.ToDouble(e, CultureInfo.InvariantCulture);
                return true;
            default:
                try
                {
                    result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    result = 0;
                    return false;
                }
        }
    }

    private enum MatchMode
    {
        Contains,
        StartsWith,
        EndsWith,
    }
}

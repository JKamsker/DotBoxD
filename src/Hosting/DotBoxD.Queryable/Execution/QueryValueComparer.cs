using System.Globalization;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Execution;

/// <summary>
/// Compares a runtime member value against a portable <see cref="QueryValue"/> for a given
/// <see cref="QueryComparisonOperator"/>. Exact numeric kinds (<see cref="QueryValueKind.Integer"/>,
/// <see cref="QueryValueKind.Decimal"/>, <see cref="QueryValueKind.UnsignedInteger"/>) compare via the widest
/// EXACT common type (<see cref="decimal"/>) so no precision is lost — only a genuine <see cref="QueryValueKind.Number"/>
/// (float/double) operand falls back to a <see cref="double"/> comparison. GUIDs compare by equality only,
/// timestamps by their UTC instant (ordered), strings ordinally (optionally ignoring case, including ordered
/// comparisons), and booleans by value. Incomparable operand pairs evaluate to <see langword="false"/> rather
/// than throwing — and an incomparable pair is also <see langword="false"/> under
/// <see cref="QueryComparisonOperator.NotEqual"/>, never a match.
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
                return TryAreNumericEqual(actual, (decimal)expected.Integer, () => (double)expected.Integer, out equal);
            case QueryValueKind.UnsignedInteger:
                return TryAreNumericEqual(actual, (decimal)expected.UnsignedInteger, () => (double)expected.UnsignedInteger, out equal);
            case QueryValueKind.Decimal:
                return TryAreNumericEqual(actual, expected.Decimal, () => (double)expected.Decimal, out equal);
            case QueryValueKind.Number:
                if (TryToDouble(actual, out var dn))
                {
                    equal = dn.Equals(expected.Number);
                    return true;
                }

                break;
            case QueryValueKind.Guid:
                if (actual is Guid g)
                {
                    equal = g == expected.Guid;
                    return true;
                }

                break;
            case QueryValueKind.Timestamp:
                if (TryToInstantTicks(actual, out var ticks))
                {
                    equal = ticks == expected.Timestamp.UtcTicks;
                    return true;
                }

                break;
        }

        equal = false;
        return false;
    }

    // Exact comparison for the integral/decimal kinds: when the runtime value is exact-numeric, compare via
    // decimal (no precision loss, full ulong range); only a float/double member falls back to double.
    private static bool TryAreNumericEqual(object actual, decimal expected, Func<double> expectedAsDouble, out bool equal)
    {
        if (TryToDecimal(actual, out var dm))
        {
            equal = dm == expected;
            return true;
        }

        if (TryToDouble(actual, out var d))
        {
            equal = d.Equals(expectedAsDouble());
            return true;
        }

        equal = false;
        return false;
    }

    private static int? Ordered(object? actual, QueryValue expected, bool ignoreCase)
    {
        switch (expected.Kind)
        {
            case QueryValueKind.Integer:
                return OrderedNumeric(actual, (decimal)expected.Integer, () => (double)expected.Integer);
            case QueryValueKind.UnsignedInteger:
                return OrderedNumeric(actual, (decimal)expected.UnsignedInteger, () => (double)expected.UnsignedInteger);
            case QueryValueKind.Decimal:
                return OrderedNumeric(actual, expected.Decimal, () => (double)expected.Decimal);
            case QueryValueKind.Number:
                return TryToDouble(actual, out var dn) ? dn.CompareTo(expected.Number) : null;
            case QueryValueKind.Timestamp:
                return TryToInstantTicks(actual, out var ticks) ? ticks.CompareTo(expected.Timestamp.UtcTicks) : null;
            case QueryValueKind.String:
                return actual is string s
                    ? string.Compare(s, expected.String, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    : null;
            default:
                // Guid (and Null/Boolean) have no meaningful ordering -> range operators evaluate false.
                return null;
        }
    }

    private static int? OrderedNumeric(object? actual, decimal expected, Func<double> expectedAsDouble)
    {
        if (TryToDecimal(actual, out var dm))
        {
            return dm.CompareTo(expected);
        }

        return TryToDouble(actual, out var d) ? d.CompareTo(expectedAsDouble()) : null;
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

    // Exact decimal view of an exact-numeric runtime value (all integral types incl. full-range ulong, decimal,
    // and enums by their underlying value). Returns false for float/double/bool/string/null so those fall back
    // to a double comparison. bool is explicitly excluded (Convert.ToDecimal(true) would yield 1m).
    private static bool TryToDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case sbyte or byte or short or ushort or int or uint or long or ulong or decimal:
                result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            case Enum e:
                result = Convert.ToDecimal(e, CultureInfo.InvariantCulture);
                return true;
            default:
                result = 0m;
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

    // Normalizes DateTime/DateTimeOffset/DateOnly to a single UTC instant in ticks (same Unspecified->UTC
    // policy as QueryValue.FromTimestamp so a captured literal and a runtime member compare consistently).
    private static bool TryToInstantTicks(object? value, out long ticks)
    {
        switch (value)
        {
            case DateTimeOffset dto:
                ticks = dto.UtcTicks;
                return true;
            case DateTime dt:
                ticks = (dt.Kind switch
                {
                    DateTimeKind.Utc => new DateTimeOffset(dt, TimeSpan.Zero),
                    DateTimeKind.Local => new DateTimeOffset(dt),
                    _ => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero),
                }).UtcTicks;
                return true;
            case DateOnly d:
                ticks = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).UtcTicks;
                return true;
            default:
                ticks = 0;
                return false;
        }
    }

    private enum MatchMode
    {
        Contains,
        StartsWith,
        EndsWith,
    }
}

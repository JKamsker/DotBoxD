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
public static partial class QueryValueComparer
{
    /// <summary>Evaluates <c>actual op expected</c>.</summary>
    public static bool Compare(object? actual, QueryComparisonOperator op, QueryValue expected, bool ignoreCase)
        => ComparisonEvaluators.TryGetValue(op, out var evaluator) &&
           evaluator(actual, expected, ignoreCase);

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

        if (EqualityEvaluators.TryGetValue(expected.Kind, out var evaluator))
        {
            return evaluator(actual, expected, ignoreCase, out equal);
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
        => OrderEvaluators.TryGetValue(expected.Kind, out var evaluator)
            ? evaluator(actual, expected, ignoreCase)
            : null;

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

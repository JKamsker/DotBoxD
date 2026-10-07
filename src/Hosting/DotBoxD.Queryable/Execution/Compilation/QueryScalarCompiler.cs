using System.Globalization;
using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Execution;

internal static class QueryScalarCompiler
{
    public static Expression? TryBuild(Expression actual, QueryComparisonOperator op, QueryValue expected, bool ignoreCase)
    {
        if (Nullable.GetUnderlyingType(actual.Type) is not null)
        {
            var value = Expression.Property(actual, "Value");
            var comparison = TryBuild(value, op, expected, ignoreCase);
            return comparison is null ? null : Expression.Condition(Expression.Property(actual, "HasValue"), comparison,
                Expression.Constant(QueryValueComparer.Compare(null, op, expected, ignoreCase)));
        }

        if (actual.Type.IsEnum)
        {
            actual = Expression.Convert(actual, Enum.GetUnderlyingType(actual.Type));
        }

        if (IsNumeric(actual.Type) && IsNumeric(expected.Kind))
        {
            return Numeric(actual, op, expected);
        }

        return Simple(actual, op, expected, ignoreCase);
    }

    private static Expression? Simple(Expression actual, QueryComparisonOperator op, QueryValue expected, bool ignoreCase)
    {
        if (actual.Type == typeof(bool) && expected.Kind == QueryValueKind.Boolean)
        {
            return Equality(actual, Expression.Constant(expected.Boolean), op);
        }

        if (actual.Type == typeof(Guid) && expected.Kind == QueryValueKind.Guid)
        {
            return Equality(actual, Expression.Constant(expected.Guid), op);
        }

        if (actual.Type == typeof(string) && expected.Kind == QueryValueKind.String &&
            op is QueryComparisonOperator.Equal or QueryComparisonOperator.NotEqual)
        {
            return StringEquality(actual, op, expected, ignoreCase);
        }

        return null;
    }

    private static Expression StringEquality(Expression actual, QueryComparisonOperator op, QueryValue expected, bool ignoreCase)
    {
        var equals = Expression.Call(typeof(string), nameof(string.Equals), Type.EmptyTypes, actual,
            Expression.Constant(expected.String),
            Expression.Constant(ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        return op == QueryComparisonOperator.Equal ? equals : Expression.Not(equals);
    }

    private static Expression? Numeric(Expression actual, QueryComparisonOperator op, QueryValue expected)
    {
        var floating = actual.Type == typeof(float) || actual.Type == typeof(double) || expected.Kind == QueryValueKind.Number;
        var bound = floating ? FloatingBound(expected) : ExactBound(ref actual, ExactValue(expected));
        actual = floating ? Expression.Convert(actual, typeof(double)) : actual;
        return Comparison(actual, bound, op);
    }

    private static Expression FloatingBound(QueryValue expected) => Expression.Constant(expected.Kind switch
    {
        QueryValueKind.Integer => (double)expected.Integer,
        QueryValueKind.UnsignedInteger => expected.UnsignedInteger,
        QueryValueKind.Decimal => (double)expected.Decimal,
        _ => expected.Number,
    });

    private static decimal ExactValue(QueryValue expected) => expected.Kind switch
    {
        QueryValueKind.Integer => (decimal)expected.Integer,
        QueryValueKind.UnsignedInteger => expected.UnsignedInteger,
        _ => expected.Decimal,
    };

    private static Expression? Comparison(Expression actual, Expression bound, QueryComparisonOperator op)
    {
        // The portable order comparer treats NaN as incomparable; != still reports true
        // against a finite literal, just as Double.Equals does.
        return op switch
        {
            QueryComparisonOperator.Equal => Expression.Equal(actual, bound),
            QueryComparisonOperator.NotEqual => Expression.NotEqual(actual, bound),
            QueryComparisonOperator.GreaterThan => Expression.GreaterThan(actual, bound),
            QueryComparisonOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(actual, bound),
            QueryComparisonOperator.LessThan => Expression.LessThan(actual, bound),
            QueryComparisonOperator.LessThanOrEqual => Expression.LessThanOrEqual(actual, bound),
            _ => null,
        };
    }

    private static Expression ExactBound(ref Expression actual, decimal number)
    {
        if (actual.Type != typeof(decimal) && decimal.Truncate(number) == number)
        {
            try
            {
                // Narrow only exact, in-range constants; cross-domain/fractional bounds use decimal.
                return Expression.Constant(Convert.ChangeType(number, actual.Type, CultureInfo.InvariantCulture), actual.Type);
            }
            catch (OverflowException)
            {
            }
        }

        actual = Expression.Convert(actual, typeof(decimal));
        return Expression.Constant(number);
    }

    private static Expression? Equality(Expression actual, Expression bound, QueryComparisonOperator op) => op switch
    {
        QueryComparisonOperator.Equal => Expression.Equal(actual, bound),
        QueryComparisonOperator.NotEqual => Expression.NotEqual(actual, bound),
        _ => null,
    };

    internal static bool IsExactNumeric(Type type) => IsIntegral(type) || type == typeof(decimal) || type.IsEnum;

    private static bool IsNumeric(Type type) => IsIntegral(type) || type == typeof(decimal) || type == typeof(float) || type == typeof(double);

    private static bool IsIntegral(Type type) => type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) ||
        type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) ||
        type == typeof(ulong);

    private static bool IsNumeric(QueryValueKind kind) => kind is QueryValueKind.Integer or QueryValueKind.UnsignedInteger or
        QueryValueKind.Decimal or QueryValueKind.Number;
}

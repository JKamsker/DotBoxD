using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryNaNComparisonTests
{
    private static readonly QueryComparisonOperator[] Operators =
    [
        QueryComparisonOperator.Equal, QueryComparisonOperator.NotEqual,
        QueryComparisonOperator.LessThan, QueryComparisonOperator.LessThanOrEqual,
        QueryComparisonOperator.GreaterThan, QueryComparisonOperator.GreaterThanOrEqual
    ];

    public static IEnumerable<object[]> Domains()
    {
        foreach (var kind in new[] { QueryValueKind.Integer, QueryValueKind.UnsignedInteger, QueryValueKind.Decimal, QueryValueKind.Number })
        {
            yield return [kind, false];
            yield return [kind, true];
        }
    }

    [Theory]
    [MemberData(nameof(Domains))]
    public void Runtime_nan_is_unordered_in_every_numeric_domain(QueryValueKind kind, bool single)
    {
        var actual = Box(single, double.NaN);
        Assert.Equal(single ? typeof(float) : typeof(double), actual!.GetType());
        foreach (var op in Operators.Skip(2))
        {
            Assert.False(QueryValueComparer.Compare(actual, op, Zero(kind), ignoreCase: false));
        }
    }

    [Theory]
    [MemberData(nameof(Domains))]
    public void Runtime_nan_remains_unequal_to_finite_values(QueryValueKind kind, bool single)
    {
        var actual = Box(single, double.NaN);

        Assert.False(QueryValueComparer.Compare(actual, QueryComparisonOperator.Equal, Zero(kind), ignoreCase: false));
        Assert.True(QueryValueComparer.Compare(actual, QueryComparisonOperator.NotEqual, Zero(kind), ignoreCase: false));
    }

    [Theory]
    [MemberData(nameof(Domains))]
    public void Finite_infinite_and_null_values_keep_native_comparison_results(QueryValueKind kind, bool single)
    {
        double?[] values = [null, double.NegativeInfinity, -1, 0, 1, double.PositiveInfinity];
        foreach (var value in values)
        {
            foreach (var op in Operators)
            {
                Assert.Equal(Native(value, op), QueryValueComparer.Compare(Box(single, value), op, Zero(kind), ignoreCase: false));
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Authored_floating_point_queries_preserve_nan_and_negated_comparisons(bool single, bool nullable)
    {
        if (single && nullable)
        {
            AssertAuthored<float?>([null, float.NaN, float.NegativeInfinity, -1, 0, 1, float.PositiveInfinity]);
        }
        else if (single)
        {
            AssertAuthored<float>([float.NaN, float.NegativeInfinity, -1, 0, 1, float.PositiveInfinity]);
        }
        else if (nullable)
        {
            AssertAuthored<double?>([null, double.NaN, double.NegativeInfinity, -1, 0, 1, double.PositiveInfinity]);
        }
        else
        {
            AssertAuthored<double>([double.NaN, double.NegativeInfinity, -1, 0, 1, double.PositiveInfinity]);
        }
    }

    [Theory]
    [InlineData(false, double.NaN)]
    [InlineData(false, double.NegativeInfinity)]
    [InlineData(false, double.PositiveInfinity)]
    [InlineData(true, double.NaN)]
    [InlineData(true, double.NegativeInfinity)]
    [InlineData(true, double.PositiveInfinity)]
    public void Non_finite_captured_literals_remain_unsupported(bool single, double value)
    {
        var parameter = LinqExpression.Parameter(typeof(Sample<double>), "e");
        var member = LinqExpression.Property(parameter, nameof(Sample<double>.Value));
        LinqExpression constant = LinqExpression.Constant(Box(single, value));
        if (single)
        {
            constant = LinqExpression.Convert(constant, typeof(double));
        }

        var predicate = LinqExpression.Lambda<Func<Sample<double>, bool>>(LinqExpression.LessThan(member, constant), parameter);

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Contains("non-finite", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertAuthored<T>(T[] values)
    {
        var scalar = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        var parameter = LinqExpression.Parameter(typeof(Sample<T>), "e");
        var member = LinqExpression.Property(parameter, nameof(Sample<T>.Value));
        var constant = LinqExpression.Constant(scalar == typeof(float) ? (object)0f : 0d, typeof(T));
        var reader = new MemberValueReader();
        foreach (var op in Operators)
        {
            var node = Enum.Parse<ExpressionType>(op.ToString());
            foreach (var negate in new[] { false, true })
            {
                LinqExpression body = LinqExpression.MakeBinary(node, member, constant);
                if (negate)
                {
                    body = LinqExpression.Not(body);
                }

                var predicate = LinqExpression.Lambda<Func<Sample<T>, bool>>(body, parameter);
                var authored = predicate.Compile();
                var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
                var compiled = QueryFilterCompiler.Compile(filter, reader);
                foreach (var value in values)
                {
                    var input = new Sample<T>(value);
                    var expected = authored(input);
                    Assert.Equal(expected, QueryFilterEvaluator.Evaluate(filter, input, reader));
                    Assert.Equal(expected, compiled(input));
                }
            }
        }
    }

    private static object? Box(bool single, double? value)
        => value is null ? null : single ? (object)(float)value.Value : value.Value;

    private static QueryValue Zero(QueryValueKind kind) => kind switch
    {
        QueryValueKind.Integer => QueryValue.FromInteger(0),
        QueryValueKind.UnsignedInteger => QueryValue.FromUnsignedInteger(0),
        QueryValueKind.Decimal => QueryValue.FromDecimal(0),
        QueryValueKind.Number => QueryValue.FromNumber(0),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static bool Native(double? value, QueryComparisonOperator op) => op switch
    {
        QueryComparisonOperator.Equal => value == 0,
        QueryComparisonOperator.NotEqual => value != 0,
        QueryComparisonOperator.LessThan => value < 0,
        QueryComparisonOperator.LessThanOrEqual => value <= 0,
        QueryComparisonOperator.GreaterThan => value > 0,
        QueryComparisonOperator.GreaterThanOrEqual => value >= 0,
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };

    private sealed record Sample<T>(T Value);
}

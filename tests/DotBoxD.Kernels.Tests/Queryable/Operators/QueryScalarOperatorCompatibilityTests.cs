using System.Linq.Expressions;
using System.Reflection;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryScalarOperatorCompatibilityTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var kind in new[] { "string", "decimal", "guid", "date", "offset", "dateOnly", "integer", "boolean" })
        {
            foreach (var nullable in new[] { false, true })
            {
                if (kind == "string" && nullable)
                {
                    continue;
                }

                yield return [kind, nullable, false];
                yield return [kind, nullable, true];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Framework_scalar_operators_preserve_nullable_and_reversed_comparisons(string kind, bool nullable, bool reversed)
    {
        switch (kind)
        {
            case "string":
                AssertComparisons<string?>([null, "alpha", "ALPHA", "beta"], reversed);
                break;
            case "decimal":
                AssertValues([1m, 2m, 3m], nullable, reversed);
                break;
            case "guid":
                AssertValues([Guid.Empty, new Guid("abcdef12-3456-7890-abcd-ef1234567890")], nullable, reversed);
                break;
            case "date":
                AssertValues([new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)], nullable, reversed);
                break;
            case "offset":
                AssertValues([DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(2)), DateTimeOffset.UnixEpoch.AddDays(1)], nullable, reversed);
                break;
            case "dateOnly":
                AssertValues([new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2)], nullable, reversed);
                break;
            case "integer":
                AssertValues([1, 2, 3], nullable, reversed);
                break;
            case "boolean":
                AssertValues([false, true], nullable, reversed);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    [Theory]
    [InlineData("string")]
    [InlineData("decimal")]
    [InlineData("date")]
    [InlineData("offset")]
    public void Equivalent_framework_static_equality_methods_remain_supported(string kind)
    {
        switch (kind)
        {
            case "string":
                AssertComparisons<string?>([null, "alpha", "beta"], reversed: false, staticEquals: true);
                break;
            case "decimal":
                AssertComparisons<decimal?>([null, 1m, 2m], reversed: false, staticEquals: true);
                break;
            case "date":
                AssertComparisons<DateTime?>([null, DateTime.UnixEpoch, DateTime.UnixEpoch.AddDays(1)], reversed: false, staticEquals: true);
                break;
            case "offset":
                AssertComparisons<DateTimeOffset?>([null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1)], reversed: false, staticEquals: true);
                break;
        }
    }

    private static void AssertValues<T>(T[] values, bool nullable, bool reversed) where T : struct
    {
        if (nullable)
        {
            AssertComparisons<T?>([null, .. values.Select(value => (T?)value)], reversed);
        }
        else
        {
            AssertComparisons(values, reversed);
        }
    }

    private static void AssertComparisons<T>(T[] values, bool reversed, bool staticEquals = false)
    {
        var scalar = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        var equalityOnly = scalar == typeof(string) || scalar == typeof(Guid) || scalar == typeof(bool);
        var operators = staticEquals ? [ExpressionType.Equal] : equalityOnly
            ? [ExpressionType.Equal, ExpressionType.NotEqual] : QueryComparisonOperatorMethodTests.ComparisonOperators;
        var method = staticEquals ? scalar.GetMethod("Equals", BindingFlags.Public | BindingFlags.Static, [scalar, scalar]) : null;
        if (staticEquals)
        {
            Assert.NotNull(method);
        }

        var parameter = LinqExpression.Parameter(typeof(ScalarEvent<T>), "e");
        var member = LinqExpression.Property(parameter, nameof(ScalarEvent<T>.Value));
        var reader = new MemberValueReader();
        foreach (var op in operators)
        {
            foreach (var captured in values)
            {
                var constant = LinqExpression.Constant(captured, typeof(T));
                var body = reversed ? LinqExpression.MakeBinary(op, constant, member, liftToNull: false, method)
                    : LinqExpression.MakeBinary(op, member, constant, liftToNull: false, method);
                var predicate = LinqExpression.Lambda<Func<ScalarEvent<T>, bool>>(body, parameter);
                var authored = predicate.Compile();
                var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
                var compiled = QueryFilterCompiler.Compile(filter, reader);
                foreach (var value in values)
                {
                    var input = new ScalarEvent<T>(value);
                    var expected = authored(input);
                    Assert.Equal(expected, QueryFilterEvaluator.Evaluate(filter, input, reader));
                    Assert.Equal(expected, compiled(input));
                }
            }
        }
    }

    private sealed record ScalarEvent<T>(T Value);
}

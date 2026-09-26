using System.Linq.Expressions;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryComparisonOperatorMethodTests
{
    public static IEnumerable<object[]> Operators()
    {
        foreach (var op in ComparisonOperators)
        {
            yield return [op, false];
            yield return [op, true];
        }
    }

    internal static ExpressionType[] ComparisonOperators { get; } =
    [
        ExpressionType.Equal, ExpressionType.NotEqual, ExpressionType.LessThan,
        ExpressionType.LessThanOrEqual, ExpressionType.GreaterThan, ExpressionType.GreaterThanOrEqual
    ];

    [Theory]
    [MemberData(nameof(Operators))]
    public void Custom_scalar_operators_are_rejected_before_capturing_operands(ExpressionType op, bool reversed)
    {
        var source = new ConstantSource(() => 1);
        var predicate = CustomPredicate(op, reversed, source);

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Contains("operator method", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("primitive", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, source.Reads);
    }

    [Theory]
    [MemberData(nameof(Operators))]
    public void Framework_operator_method_must_match_the_expression_node(ExpressionType op, bool nullable)
    {
        if (nullable)
        {
            AssertMismatchedMethod<decimal?>(op);
        }
        else
        {
            AssertMismatchedMethod<decimal>(op);
        }
    }

    private static void AssertMismatchedMethod<T>(ExpressionType op)
    {
        var parameter = LinqExpression.Parameter(typeof(ScalarEvent<T>), "e");
        var member = LinqExpression.Property(parameter, nameof(ScalarEvent<T>.Value));
        var constant = LinqExpression.Constant(2m, typeof(T));
        var other = op == ExpressionType.Equal ? "op_LessThan" : "op_Equality";
        var method = typeof(decimal).GetMethod(other, [typeof(decimal), typeof(decimal)])!;
        var body = LinqExpression.MakeBinary(op, member, constant, liftToNull: false, method);
        var predicate = LinqExpression.Lambda<Func<ScalarEvent<T>, bool>>(body, parameter);

        var error = Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Contains("operator method", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Foreign_equality_methods_on_supported_scalar_operands_are_rejected()
    {
        var parameter = LinqExpression.Parameter(typeof(ScalarEvent<decimal>), "e");
        var member = LinqExpression.Property(parameter, nameof(ScalarEvent<decimal>.Value));
        var method = typeof(CustomEquality).GetMethod(nameof(CustomEquality.Equals), [typeof(decimal), typeof(decimal)])!;
        var body = LinqExpression.Equal(member, LinqExpression.Constant(2m), liftToNull: false, method);
        var predicate = LinqExpression.Lambda<Func<ScalarEvent<decimal>, bool>>(body, parameter);
        Assert.True(predicate.Compile()(new ScalarEvent<decimal>(1m)));

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Contains("operator method", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Unsupported_operator_diagnostic_precedes_throwing_or_canceled_constant_getters(bool reversed, bool cancellation)
    {
        Exception failure = cancellation ? new OperationCanceledException() : new InvalidOperationException("getter");
        var source = new ConstantSource(() => throw failure);
        var predicate = CustomPredicate(ExpressionType.Equal, reversed, source);

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Contains("operator method", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(error.InnerException);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public void Primitive_members_remain_an_explicit_alternative_to_custom_operators()
    {
        Expression<Func<CustomEvent, bool>> predicate = e => e.Value.Value == 1;
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var value in new[] { 0, 1, 2 })
        {
            var input = new CustomEvent(new CustomScalar(value));
            Assert.Equal(value == 1, QueryFilterEvaluator.Evaluate(filter, input, reader));
            Assert.Equal(value == 1, compiled(input));
        }
    }

    private static Expression<Func<CustomEvent, bool>> CustomPredicate(ExpressionType op, bool reversed, ConstantSource source)
    {
        var parameter = LinqExpression.Parameter(typeof(CustomEvent), "e");
        var member = LinqExpression.Property(parameter, nameof(CustomEvent.Value));
        var constant = LinqExpression.Property(LinqExpression.Constant(source), nameof(ConstantSource.Value));
        var body = reversed ? LinqExpression.MakeBinary(op, constant, member) : LinqExpression.MakeBinary(op, member, constant);
        return LinqExpression.Lambda<Func<CustomEvent, bool>>(body, parameter);
    }

    private sealed record CustomEvent(CustomScalar Value);
    private sealed record ScalarEvent<T>(T Value);

    private static class CustomEquality
    {
        public static bool Equals(decimal left, decimal right) => left != right;
    }

    private sealed class ConstantSource(Func<int> factory)
    {
        public int Reads { get; private set; }

        public int Value
        {
            get
            {
                Reads++;
                return factory();
            }
        }
    }

    private readonly record struct CustomScalar(int Value)
    {
        public static bool operator ==(CustomScalar left, int right) => left.Value == right;
        public static bool operator !=(CustomScalar left, int right) => left.Value != right;
        public static bool operator <(CustomScalar left, int right) => left.Value < right;
        public static bool operator <=(CustomScalar left, int right) => left.Value <= right;
        public static bool operator >(CustomScalar left, int right) => left.Value > right;
        public static bool operator >=(CustomScalar left, int right) => left.Value >= right;
        public static bool operator ==(int left, CustomScalar right) => left == right.Value;
        public static bool operator !=(int left, CustomScalar right) => left != right.Value;
        public static bool operator <(int left, CustomScalar right) => left < right.Value;
        public static bool operator <=(int left, CustomScalar right) => left <= right.Value;
        public static bool operator >(int left, CustomScalar right) => left > right.Value;
        public static bool operator >=(int left, CustomScalar right) => left >= right.Value;
    }
}

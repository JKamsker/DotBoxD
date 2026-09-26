using System.Linq.Expressions;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryLogicalOperatorMethodTests
{
    public static IEnumerable<object[]> CustomMethods()
    {
        foreach (var method in new[] { nameof(LogicalMethods.Identity), nameof(LogicalMethods.AlwaysTrue), nameof(LogicalMethods.AlwaysFalse) })
        {
            yield return [method, false];
            yield return [method, true];
        }
    }

    [Theory]
    [MemberData(nameof(CustomMethods))]
    public void Custom_not_methods_are_rejected_before_translating_the_operand(string method, bool comparison)
    {
        var source = new ConstantSource(() => 1);
        var predicate = Predicate(method, comparison, source);
        Assert.Equal(method != nameof(LogicalMethods.AlwaysFalse), predicate.Compile()(new Sample(true, 1)));
        var priorReads = source.Reads;

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Contains("operator method", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(priorReads, source.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_not_diagnostic_precedes_throwing_or_canceled_captured_operands(bool cancellation)
    {
        Exception failure = cancellation ? new OperationCanceledException() : new InvalidOperationException("getter");
        var source = new ConstantSource(() => throw failure);
        var predicate = Predicate(nameof(LogicalMethods.Identity), comparison: true, source);

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Contains("operator method", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(error.InnerException);
        Assert.Equal(0, source.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Intrinsic_not_preserves_results_and_single_constant_capture(bool comparison)
    {
        var source = new ConstantSource(() => 1);
        var predicate = Predicate(methodName: null, comparison, source);
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        Assert.Equal(comparison ? 1 : 0, source.Reads);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var input in new[] { new Sample(true, 1), new Sample(false, 2) })
        {
            var expected = comparison ? input.Amount != 1 : !input.Enabled;
            Assert.Equal(expected, QueryFilterEvaluator.Evaluate(filter, input, reader));
            Assert.Equal(expected, compiled(input));
        }

        Assert.Equal(comparison ? 1 : 0, source.Reads);
    }

    private static Expression<Func<Sample, bool>> Predicate(string? methodName, bool comparison, ConstantSource source)
    {
        var parameter = LinqExpression.Parameter(typeof(Sample), "e");
        var member = LinqExpression.Property(parameter, comparison ? nameof(Sample.Amount) : nameof(Sample.Enabled));
        LinqExpression operand = comparison
            ? LinqExpression.Equal(member, LinqExpression.Property(LinqExpression.Constant(source), nameof(ConstantSource.Value)))
            : member;
        var method = methodName is null ? null : typeof(LogicalMethods).GetMethod(methodName);
        var body = LinqExpression.Not(operand, method);
        return LinqExpression.Lambda<Func<Sample, bool>>(body, parameter);
    }

    private sealed record Sample(bool Enabled, int Amount);

    private static class LogicalMethods
    {
        public static bool Identity(bool value) => value;
        public static bool AlwaysTrue(bool _) => true;
        public static bool AlwaysFalse(bool _) => false;
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
}

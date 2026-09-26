using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryBooleanConversionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Event_boolean_conversions_are_rejected(bool logical)
    {
        Expression<Func<BooleanEvent, bool>> predicate = logical
            ? e => e.Enabled && (bool)(InvertedBoolean)e.Enabled
            : e => (bool)(InvertedBoolean)e.Enabled;
        Assert.False(predicate.Compile()(new BooleanEvent(true)));

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Contains("casts", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Converted_boolean_literals_preserve_their_results(bool literal)
    {
        Expression<Func<BooleanEvent, bool>> predicate = literal
            ? _ => (bool)(InvertedBoolean)true
            : _ => (bool)(InvertedBoolean)false;

        AssertResult(ExpressionQueryTranslator.TranslateFilter(predicate), expected: !literal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Captured_boolean_conversion_runs_once(bool expected)
    {
        var source = new ConvertedBoolean(() => expected);

        var filter = ExpressionQueryTranslator.TranslateFilter<BooleanEvent>(_ => (bool)source);

        Assert.Equal(1, source.Reads);
        AssertResult(filter, expected);
        Assert.Equal(1, source.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Boolean_conversion_failures_are_preserved(bool cancellation)
    {
        Exception failure = cancellation ? new OperationCanceledException() : new InvalidOperationException("conversion");
        var source = new ConvertedBoolean(() => throw failure);
        if (cancellation)
        {
            var error = Assert.Throws<OperationCanceledException>(() =>
                ExpressionQueryTranslator.TranslateFilter<BooleanEvent>(_ => (bool)source));
            Assert.Same(failure, error);
        }
        else
        {
            var error = Assert.Throws<QueryTranslationException>(() =>
                ExpressionQueryTranslator.TranslateFilter<BooleanEvent>(_ => (bool)source));
            Assert.Same(failure, error.InnerException);
        }

        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public void Direct_boolean_member_predicates_remain_supported()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<BooleanEvent>(e => e.Enabled);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var enabled in new[] { false, true })
        {
            var value = new BooleanEvent(enabled);
            Assert.Equal(enabled, QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(enabled, compiled(value));
        }
    }

    private static void AssertResult(QueryFilter filter, bool expected)
    {
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var enabled in new[] { false, true })
        {
            var value = new BooleanEvent(enabled);
            Assert.Equal(expected, QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(expected, compiled(value));
        }
    }

    public sealed record BooleanEvent(bool Enabled);

    private readonly record struct InvertedBoolean(bool Value)
    {
        public static explicit operator InvertedBoolean(bool value) => new(!value);
        public static explicit operator bool(InvertedBoolean value) => value.Value;
    }

    private sealed class ConvertedBoolean(Func<bool> factory)
    {
        public int Reads { get; private set; }

        public static explicit operator bool(ConvertedBoolean source) => source.Read();

        private bool Read()
        {
            Reads++;
            return factory();
        }
    }
}

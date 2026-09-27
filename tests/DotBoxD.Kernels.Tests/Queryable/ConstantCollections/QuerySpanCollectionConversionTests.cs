using System.Collections;
using System.Linq.Expressions;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QuerySpanCollectionConversionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Span_conversion_is_captured_once(bool readOnly)
    {
        var source = new ConvertingSpan(() => [2]);
        Expression<Func<AttackTestEvent, bool>> predicate = readOnly
            ? e => ((ReadOnlySpan<int>)source).Contains(e.Damage)
            : e => ((Span<int>)source).Contains(e.Damage);

        AssertCapturedMembership(source, predicate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Span_conversion_preserves_the_selected_slice(bool readOnly)
    {
        var source = new ConvertingSpan(() => [1, 2, 3], offset: 1, length: 1);
        Expression<Func<AttackTestEvent, bool>> predicate = readOnly
            ? e => ((ReadOnlySpan<int>)source).Contains(e.Damage)
            : e => ((Span<int>)source).Contains(e.Damage);

        AssertCapturedMembership(source, predicate);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Span_conversion_failures_are_preserved(bool readOnly, bool cancellation)
    {
        Exception failure = cancellation ? new OperationCanceledException() : new InvalidOperationException("conversion");
        var source = new ConvertingSpan(() => throw failure);
        Expression<Func<AttackTestEvent, bool>> predicate = readOnly
            ? e => ((ReadOnlySpan<int>)source).Contains(e.Damage)
            : e => ((Span<int>)source).Contains(e.Damage);

        if (cancellation)
        {
            var error = Assert.Throws<OperationCanceledException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));
            Assert.Same(failure, error);
        }
        else
        {
            var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));
            Assert.Same(failure, error.InnerException);
        }

        Assert.Equal(1, source.Reads);
    }

    private static void AssertCapturedMembership(ConvertingSpan source, Expression<Func<AttackTestEvent, bool>> predicate)
    {
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        Assert.Equal(1, source.Reads);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var damage in new[] { 1, 2, 3 })
        {
            var value = new AttackTestEvent("alice", "target", damage, 1);
            Assert.Equal(damage == 2, QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(damage == 2, compiled(value));
        }

        Assert.Equal(1, source.Reads);
    }

    private sealed class ConvertingSpan(Func<int[]> factory, int offset = 0, int? length = null) : IEnumerable<int>
    {
        public int Reads { get; private set; }

        public static explicit operator Span<int>(ConvertingSpan source) => source.ReadSpan();
        public static explicit operator ReadOnlySpan<int>(ConvertingSpan source) => source.ReadSpan();

        public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)new[] { 1 }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private Span<int> ReadSpan()
        {
            Reads++;
            var values = factory();
            return values.AsSpan(offset, length ?? values.Length - offset);
        }
    }
}

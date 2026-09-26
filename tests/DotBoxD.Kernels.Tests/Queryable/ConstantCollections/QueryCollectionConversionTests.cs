using System.Collections;
using System.Linq.Expressions;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryCollectionConversionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Converted_collection_is_captured_once(bool staticContains, bool array)
    {
        var source = new ConvertingList(() => [2]);
        Expression<Func<AttackTestEvent, bool>> predicate = (staticContains, array) switch
        {
            (true, true) => e => Enumerable.Contains((int[])source, e.Damage),
            (false, true) => e => ((int[])source).Contains(e.Damage),
            (true, false) => e => Enumerable.Contains((List<int>)source, e.Damage),
            (false, false) => e => ((List<int>)source).Contains(e.Damage)
        };

        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        Assert.Equal(1, source.Conversions);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var damage in new[] { 1, 2, 3 })
        {
            var value = new AttackTestEvent("alice", "target", damage, 1);
            Assert.Equal(damage == 2, QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(damage == 2, compiled(value));
        }

        Assert.Equal(1, source.Conversions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Conversion_failures_are_preserved(bool staticContains, bool cancellation)
    {
        Exception failure = cancellation ? new OperationCanceledException() : new InvalidOperationException("conversion");
        var source = new ConvertingList(() => throw failure);
        Expression<Func<AttackTestEvent, bool>> predicate = staticContains
            ? e => Enumerable.Contains((List<int>)source, e.Damage)
            : e => ((List<int>)source).Contains(e.Damage);

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

        Assert.Equal(1, source.Conversions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_reference_conversion_is_reported(bool staticContains)
    {
        object source = new[] { 1 };
        Expression<Func<AttackTestEvent, bool>> predicate = staticContains
            ? e => Enumerable.Contains((List<int>)source, e.Damage)
            : e => ((List<int>)source).Contains(e.Damage);

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.IsType<InvalidCastException>(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Converted_collection_comparer_is_validated(bool staticContains)
    {
        var source = new ConvertingSet();
        Expression<Func<AttackTestEvent, bool>> predicate = staticContains
            ? e => Enumerable.Contains((HashSet<string>)source, e.AttackerId)
            : e => ((HashSet<string>)source).Contains(e.AttackerId);

        var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));

        Assert.Contains("comparer", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, source.Conversions);
    }

    private sealed class ConvertingList(Func<List<int>> factory) : IEnumerable<int>
    {
        public int Conversions { get; private set; }

        public static explicit operator List<int>(ConvertingList source) => source.Convert();
        public static explicit operator int[](ConvertingList source) => source.Convert().ToArray();

        public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)new[] { 1 }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private List<int> Convert()
        {
            Conversions++;
            return factory();
        }
    }

    private sealed class ConvertingSet : IEnumerable<string>
    {
        public int Conversions { get; private set; }

        public static explicit operator HashSet<string>(ConvertingSet source)
        {
            source.Conversions++;
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alice" };
        }

        public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)new[] { "original" }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

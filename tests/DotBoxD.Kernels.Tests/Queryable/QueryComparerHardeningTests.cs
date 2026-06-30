using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryComparerHardeningTests
{
    [Fact]
    public void Contains_over_non_string_custom_equality_comparer_is_rejected()
    {
        var watched = new HashSet<int>(new ParityComparer()) { 1 };
        Assert.True(watched.Comparer.Equals(1, 3));

        var ex = Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.Damage)));

        Assert.Contains("comparer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Contains_over_default_non_string_hashset_still_lowers_to_in()
    {
        var watched = new HashSet<int> { 1, 2 };
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.Damage));
        Assert.Equal(QueryFilterKind.In, filter.Kind);
    }

    [Fact]
    public void Contains_over_custom_comparer_equal_to_default_is_rejected()
    {
        var watched = new HashSet<int>(new DefaultEqualParityComparer()) { 1 };
        Assert.True(watched.Comparer.Equals(EqualityComparer<int>.Default));

        var ex = Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.Damage)));

        Assert.Contains("comparer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ParityComparer : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) => (x & 1) == (y & 1);

        public int GetHashCode(int obj) => obj & 1;
    }

    private sealed class DefaultEqualParityComparer : IEqualityComparer<int>
    {
        public bool Equals(int x, int y) => (x & 1) == (y & 1);

        public int GetHashCode(int obj) => obj & 1;

        public override bool Equals(object? obj) => ReferenceEquals(obj, EqualityComparer<int>.Default);

        public override int GetHashCode() => 0;
    }
}

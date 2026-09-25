using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryComparerProbeExceptionTests
{
    [Fact]
    public void Contains_over_throwing_comparer_is_rejected_without_invoking_it()
    {
        var comparer = new ThrowingStringComparer();
        var watched = new HashSet<string>(comparer) { "player-1" };

        var ex = Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.AttackerId)));

        Assert.Contains("comparer", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, comparer.EqualsCallCount);
    }

    private sealed class ThrowingStringComparer : IEqualityComparer<string>
    {
        public int EqualsCallCount { get; private set; }

        public bool Equals(string? x, string? y)
        {
            EqualsCallCount++;
            throw new InvalidOperationException("comparer exploded");
        }

        public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(obj);
    }
}

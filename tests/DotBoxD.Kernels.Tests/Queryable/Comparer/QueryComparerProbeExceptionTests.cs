using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryComparerProbeExceptionTests
{
    [Fact]
    public void Contains_over_comparer_that_throws_during_probe_is_wrapped()
    {
        var watched = new HashSet<string>(new ThrowingStringComparer()) { "player-1" };

        var ex = Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.AttackerId)));

        Assert.Contains("comparer", ex.Message, StringComparison.OrdinalIgnoreCase);

        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal("comparer exploded", inner.Message);
    }

    private sealed class ThrowingStringComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => throw new InvalidOperationException("comparer exploded");

        public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(obj);
    }
}

using System.Collections;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryConstantCollectionSurpriseTests
{
    [Fact]
    public void Contains_over_collection_with_throwing_enumerator_is_wrapped()
    {
        var ids = new ThrowingEnumerable();

        var ex = Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => ids.Contains(e.AttackerId)));

        Assert.Contains("constant collection", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Contains", ex.Message, StringComparison.OrdinalIgnoreCase);

        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal("enumerator failed", inner.Message);
    }

    private sealed class ThrowingEnumerable : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator() => new ThrowingEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ThrowingEnumerator : IEnumerator<string>
    {
        public string Current => "unused";

        object IEnumerator.Current => Current;

        public bool MoveNext() => throw new InvalidOperationException("enumerator failed");

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }
}

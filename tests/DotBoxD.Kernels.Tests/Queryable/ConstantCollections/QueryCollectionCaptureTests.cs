using System.Linq.Expressions;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryCollectionCaptureTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Contains_captures_one_collection_instance(bool property, bool staticContains)
    {
        var source = new ChangingCollection();
        Expression<Func<AttackTestEvent, bool>> predicate = (property, staticContains) switch
        {
            (true, true) => e => Enumerable.Contains(source.Values, e.AttackerId),
            (true, false) => e => source.Values.Contains(e.AttackerId),
            (false, true) => e => Enumerable.Contains(source.Next(), e.AttackerId),
            (false, false) => e => source.Next().Contains(e.AttackerId)
        };

        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        Assert.Equal(1, source.Reads);

        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        var first = new AttackTestEvent("alice", "target", 1, 1);
        var later = new AttackTestEvent("bob", "target", 1, 1);
        Assert.True(QueryFilterEvaluator.Evaluate(filter, first, reader));
        Assert.True(compiled(first));
        Assert.False(QueryFilterEvaluator.Evaluate(filter, later, reader));
        Assert.False(compiled(later));
        Assert.Equal(1, source.Reads);
    }

    private sealed class ChangingCollection
    {
        public int Reads { get; private set; }

        public HashSet<string> Values => Next();

        public HashSet<string> Next() => new(StringComparer.Ordinal)
        {
            ++Reads == 1 ? "alice" : "bob"
        };
    }
}

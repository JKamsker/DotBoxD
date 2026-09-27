using System.Linq.Expressions;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryHiddenCollectionComparerTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var kind in new[] { "hash-set", "sorted-set", "dictionary-keys", "sorted-dictionary-keys", "sorted-list-keys", "collection", "read-only-collection", "read-only-set" })
        {
            foreach (var custom in new[] { false, true })
            {
                foreach (var throwing in new[] { false, true })
                {
                    foreach (var staticContains in new[] { false, true })
                    {
                        yield return [kind, custom, throwing, staticContains];
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Membership_uses_the_framework_comparer_without_reading_hidden_properties(
        string kind, bool custom, bool throwing, bool staticContains)
    {
        var state = new HiddenComparerCollections.ComparerState(custom, throwing);
        var values = HiddenComparerCollections.Create(kind, state);
        var native = values.Contains("ALICE");
        Assert.Equal(custom, native);
        Expression<Func<AttackTestEvent, bool>> predicate = staticContains
            ? e => Enumerable.Contains(values, e.AttackerId) : e => values.Contains(e.AttackerId);

        AssertTranslation(predicate, custom);
        Assert.Equal(0, state.Reads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Unrelated_list_comparer_properties_do_not_affect_membership(bool throwing, bool staticContains)
    {
        var state = new HiddenComparerCollections.ComparerState(custom: false, throwing);
        var values = new HiddenComparerCollections.HiddenList(state);
        Expression<Func<AttackTestEvent, bool>> predicate = staticContains
            ? e => Enumerable.Contains(values, e.AttackerId) : e => values.Contains(e.AttackerId);

        AssertTranslation(predicate, custom: false);
        Assert.Equal(0, state.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Framework_declared_nonvirtual_calls_read_the_framework_comparer(bool custom)
    {
        var state = new HiddenComparerCollections.ComparerState(custom, throwing: true);
        HashSet<string> values = new HiddenComparerCollections.HiddenHashSet(state) { "alice" };

        AssertTranslation(e => values.Contains(e.AttackerId), custom);
        Assert.Equal(0, state.Reads);
    }

    private static void AssertTranslation(Expression<Func<AttackTestEvent, bool>> predicate, bool custom)
    {
        if (custom)
        {
            var error = Assert.Throws<QueryTranslationException>(() => ExpressionQueryTranslator.TranslateFilter(predicate));
            Assert.Contains("comparer", error.Message, StringComparison.OrdinalIgnoreCase);
            return;
        }

        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        var authored = predicate.Compile();
        foreach (var name in new[] { "alice", "ALICE", "missing" })
        {
            var value = new AttackTestEvent(name, "target", 1, 1);
            Assert.Equal(authored(value), QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(authored(value), compiled(value));
        }
    }
}

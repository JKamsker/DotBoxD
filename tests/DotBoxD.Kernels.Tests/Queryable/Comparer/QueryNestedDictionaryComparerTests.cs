using System.Collections.ObjectModel;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryNestedDictionaryComparerTests
{
    [Theory]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(8, false)]
    [InlineData(8, true)]
    [InlineData(32, false)]
    [InlineData(32, true)]
    public void Nested_key_views_preserve_comparer_validation(int depth, bool staticContains)
    {
        var source = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["alice"] = 1 };
        var keys = Wrap(source, depth).Keys;
        Assert.True(keys.Contains("ALICE"));
        var staticMatches = Enumerable.Contains(keys, "ALICE");
        Assert.True(staticMatches);

        var error = Assert.Throws<QueryTranslationException>(() => Translate(keys, staticContains));

        Assert.Contains("comparer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(8, false)]
    [InlineData(8, true)]
    [InlineData(32, false)]
    [InlineData(32, true)]
    public void Nested_ordinal_key_views_preserve_membership(int depth, bool staticContains)
    {
        var source = new Dictionary<string, int>(StringComparer.Ordinal) { ["alice"] = 1 };
        var keys = Wrap(source, depth).Keys;
        var filter = Translate(keys, staticContains);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);

        foreach (var name in new[] { "alice", "ALICE", "bob" })
        {
            var value = new AttackTestEvent(name, "target", 1, 1);
            Assert.Equal(keys.Contains(name), QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(keys.Contains(name), compiled(value));
        }
    }

    [Fact]
    public void Nested_default_integer_key_views_remain_supported()
    {
        var keys = Wrap(new Dictionary<int, string> { [1] = "one", [3] = "three" }, 32).Keys;
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => keys.Contains(e.Damage));
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);

        foreach (var damage in new[] { 1, 2, 3 })
        {
            var value = new AttackTestEvent("alice", "target", damage, 1);
            Assert.Equal(keys.Contains(damage), QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(keys.Contains(damage), compiled(value));
        }
    }

    private static IDictionary<TKey, TValue> Wrap<TKey, TValue>(IDictionary<TKey, TValue> source, int depth)
        where TKey : notnull
    {
        for (var index = 0; index < depth; index++)
        {
            source = new ReadOnlyDictionary<TKey, TValue>(source);
        }

        return source;
    }

    private static QueryFilter Translate(ICollection<string> keys, bool staticContains) => staticContains
        ? ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => Enumerable.Contains(keys, e.AttackerId))
        : ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => keys.Contains(e.AttackerId));
}

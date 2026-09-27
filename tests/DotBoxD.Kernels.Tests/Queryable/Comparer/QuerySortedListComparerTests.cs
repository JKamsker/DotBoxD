using System.Collections.ObjectModel;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QuerySortedListComparerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Sorted_list_key_comparer_is_validated(bool readOnly, bool staticContains)
    {
        var keys = CreateKeys(readOnly, StringComparer.OrdinalIgnoreCase);
        Assert.True(keys.Contains("ALICE"));
        var staticMatches = Enumerable.Contains(keys, "ALICE");
        Assert.True(staticMatches);

        var error = Assert.Throws<QueryTranslationException>(() => Translate(keys, staticContains));

        Assert.Contains("comparer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Sorted_list_ordinal_keys_preserve_membership(bool readOnly, bool staticContains)
    {
        var keys = CreateKeys(readOnly, StringComparer.Ordinal);
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
    public void Sorted_list_default_integer_keys_remain_supported()
    {
        var keys = new SortedList<int, string> { [1] = "one", [3] = "three" }.Keys;
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

    [Fact]
    public void Sorted_list_default_culture_sensitive_string_comparer_is_rejected()
    {
        var keys = new SortedList<string, int> { ["alice"] = 1 }.Keys;

        var error = Assert.Throws<QueryTranslationException>(() => Translate(keys, staticContains: true));

        Assert.Contains("comparer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ICollection<string> CreateKeys(bool readOnly, StringComparer comparer)
    {
        var source = new SortedList<string, int>(comparer) { ["alice"] = 1 };
        return readOnly ? new ReadOnlyDictionary<string, int>(source).Keys : source.Keys;
    }

    private static QueryFilter Translate(ICollection<string> keys, bool staticContains) => staticContains
        ? ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => Enumerable.Contains(keys, e.AttackerId))
        : ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => keys.Contains(e.AttackerId));
}

using System.Collections.Immutable;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryImmutableCollectionComparerTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Immutable_set_custom_equality_is_rejected(bool sorted, bool builder, bool staticContains)
    {
        var watched = CreateStrings(sorted, builder, StringComparer.OrdinalIgnoreCase);
        Assert.True(watched.Contains("ALICE"));
        var staticMatches = Enumerable.Contains(watched, "ALICE");
        Assert.True(staticMatches);

        var error = Assert.Throws<QueryTranslationException>(() => staticContains
            ? ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => Enumerable.Contains(watched, e.AttackerId))
            : ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.AttackerId)));

        Assert.Contains("comparer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Immutable_set_ordinal_equality_preserves_membership(bool sorted, bool builder)
    {
        var watched = CreateStrings(sorted, builder, StringComparer.Ordinal);
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.AttackerId));

        AssertStringMembership(filter, watched.Contains);
    }

    [Fact]
    public void Immutable_hash_set_default_string_equality_remains_supported()
    {
        var watched = ImmutableHashSet.Create("alice");
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => Enumerable.Contains(watched, e.AttackerId));

        AssertStringMembership(filter, watched.Contains);
    }

    [Fact]
    public void Immutable_sorted_set_default_integer_equality_remains_supported()
    {
        var watched = ImmutableSortedSet.Create(1, 3);
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => Enumerable.Contains(watched, e.Damage));
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);

        foreach (var damage in new[] { 1, 2, 3 })
        {
            var value = new AttackTestEvent("alice", "target", damage, 1);
            Assert.Equal(watched.Contains(damage), QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(watched.Contains(damage), compiled(value));
        }
    }

    private static ICollection<string> CreateStrings(bool sorted, bool builder, StringComparer comparer)
    {
        if (sorted)
        {
            var set = ImmutableSortedSet.Create(comparer, "alice");
            return builder ? set.ToBuilder() : set;
        }

        var hashSet = ImmutableHashSet.Create(comparer, "alice");
        return builder ? hashSet.ToBuilder() : hashSet;
    }

    private static void AssertStringMembership(QueryFilter filter, Func<string, bool> contains)
    {
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        Assert.Equal(QueryFilterKind.In, filter.Kind);
        foreach (var name in new[] { "alice", "ALICE", "bob" })
        {
            var value = new AttackTestEvent(name, "target", 1, 1);
            Assert.Equal(contains(name), QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(contains(name), compiled(value));
        }
    }
}

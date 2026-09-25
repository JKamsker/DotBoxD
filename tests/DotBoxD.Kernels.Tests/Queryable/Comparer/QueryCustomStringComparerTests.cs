using System.Collections.ObjectModel;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryCustomStringComparerTests
{
    [Theory]
    [InlineData("hash-set", false)]
    [InlineData("hash-set", true)]
    [InlineData("sorted-set", false)]
    [InlineData("sorted-set", true)]
    [InlineData("dictionary", false)]
    [InlineData("dictionary", true)]
    [InlineData("sorted-dictionary", false)]
    [InlineData("sorted-dictionary", true)]
    [InlineData("read-only-dictionary", false)]
    [InlineData("read-only-dictionary", true)]
    public void Contains_with_custom_string_equality_is_rejected(string collectionKind, bool staticContains)
    {
        var watched = CreateCollection(collectionKind);
        Assert.True(watched.Contains(" alice "));

        var error = Assert.Throws<QueryTranslationException>(() => staticContains
            ? ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => Enumerable.Contains(watched, e.AttackerId))
            : ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.AttackerId)));

        Assert.Contains("comparer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Known_ordinal_equality_keeps_membership_semantics(bool explicitOrdinal)
    {
        var watched = explicitOrdinal ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>();
        watched.Add("alice");
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.AttackerId));

        AssertOrdinalMembership(filter, watched.Contains);
    }

    [Fact]
    public void Default_object_equality_with_string_values_keeps_membership_semantics()
    {
        var watched = new HashSet<object> { "alice" };
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.AttackerId));

        AssertOrdinalMembership(filter, name => watched.Contains(name));
    }

    private static void AssertOrdinalMembership(QueryFilter filter, Func<string, bool> contains)
    {
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);

        Assert.Equal(QueryFilterKind.In, filter.Kind);
        foreach (var name in new[] { "alice", " alice ", "ALICE", "bob" })
        {
            var value = new AttackTestEvent(name, "target", 1, 1);
            Assert.Equal(contains(name), QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(contains(name), compiled(value));
        }
    }

    private static ICollection<string> CreateCollection(string kind)
    {
        var comparer = new TrimmingComparer();
        return kind switch
        {
            "hash-set" => new HashSet<string>(comparer) { "alice" },
            "sorted-set" => new SortedSet<string>(comparer) { "alice" },
            "dictionary" => new Dictionary<string, int>(comparer) { ["alice"] = 1 }.Keys,
            "sorted-dictionary" => new SortedDictionary<string, int>(comparer) { ["alice"] = 1 }.Keys,
            "read-only-dictionary" => new ReadOnlyDictionary<string, int>(
                new Dictionary<string, int>(comparer) { ["alice"] = 1 }).Keys,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private sealed class TrimmingComparer : StringComparer
    {
        public override int Compare(string? x, string? y) => string.CompareOrdinal(x?.Trim(), y?.Trim());

        public override bool Equals(string? x, string? y) => string.Equals(x?.Trim(), y?.Trim(), StringComparison.Ordinal);

        public override int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(obj.Trim());
    }
}

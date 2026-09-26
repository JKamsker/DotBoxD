using System.Collections.ObjectModel;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryWrappedCollectionComparerTests
{
    [Theory]
    [InlineData("collection", false)]
    [InlineData("collection", true)]
    [InlineData("read-only-collection", false)]
    [InlineData("read-only-collection", true)]
    [InlineData("read-only-set", false)]
    [InlineData("read-only-set", true)]
    [InlineData("derived", false)]
    [InlineData("derived", true)]
    public void Wrapped_membership_comparer_is_validated(string kind, bool staticContains)
    {
        var values = CreateStrings(kind, StringComparer.OrdinalIgnoreCase);
        Assert.True(values.Contains("ALICE"));
        var staticMatches = Enumerable.Contains(values, "ALICE");
        Assert.True(staticMatches);

        var error = Assert.Throws<QueryTranslationException>(() => Translate(values, staticContains));

        Assert.Contains("comparer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("collection", false)]
    [InlineData("collection", true)]
    [InlineData("read-only-collection", false)]
    [InlineData("read-only-collection", true)]
    [InlineData("read-only-set", false)]
    [InlineData("read-only-set", true)]
    [InlineData("derived", false)]
    [InlineData("derived", true)]
    public void Wrapped_ordinal_membership_remains_supported(string kind, bool staticContains)
    {
        var values = CreateStrings(kind, StringComparer.Ordinal);
        var filter = Translate(values, staticContains);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);

        foreach (var name in new[] { "alice", "ALICE", "bob" })
        {
            var value = new AttackTestEvent(name, "target", 1, 1);
            Assert.Equal(values.Contains(name), QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(values.Contains(name), compiled(value));
        }
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("read-only-collection")]
    [InlineData("read-only-set")]
    public void Wrapped_default_integer_membership_remains_supported(string kind)
    {
        var keys = new SortedList<int, string> { [1] = "one", [3] = "three" }.Keys;
        ICollection<int> values = kind switch
        {
            "collection" => new Collection<int>(keys),
            "read-only-collection" => new ReadOnlyCollection<int>(keys),
            "read-only-set" => new ReadOnlySet<int>(new HashSet<int> { 1, 3 }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => values.Contains(e.Damage));
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);

        foreach (var damage in new[] { 1, 2, 3 })
        {
            var value = new AttackTestEvent("alice", "target", damage, 1);
            Assert.Equal(values.Contains(damage), QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(values.Contains(damage), compiled(value));
        }
    }

    private static ICollection<string> CreateStrings(string kind, StringComparer comparer)
    {
        var keys = new SortedList<string, int>(comparer) { ["alice"] = 1 }.Keys;
        return kind switch
        {
            "collection" => new Collection<string>(keys),
            "read-only-collection" => new ReadOnlyCollection<string>(keys),
            "read-only-set" => new ReadOnlySet<string>(new HashSet<string>(comparer) { "alice" }),
            "derived" => new DerivedReadOnlyCollection(keys),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static QueryFilter Translate(ICollection<string> values, bool staticContains) => staticContains
        ? ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => Enumerable.Contains(values, e.AttackerId))
        : ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => values.Contains(e.AttackerId));

    private sealed class DerivedReadOnlyCollection(IList<string> items) : ReadOnlyCollection<string>(items);
}

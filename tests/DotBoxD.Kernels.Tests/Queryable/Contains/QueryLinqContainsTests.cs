using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryLinqContainsTests
{
    public static IEnumerable<object[]> ForwardingCases() =>
        LinqContainsCases.ForwardingOperators.SelectMany(kind => new[] { new object[] { kind, false }, new object[] { kind, true } });

    public static IEnumerable<object[]> DefaultCases() =>
        LinqContainsCases.ForwardingOperators.Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(ForwardingCases))]
    public void Delegated_custom_membership_is_rejected(string kind, bool customImplementation)
    {
        IEnumerable<string> source = customImplementation
            ? new LinqContainsCases.CustomList()
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alice" };
        var values = LinqContainsCases.Forward(kind, source);
        var authoredMatch = values.Contains("ALICE");
        Assert.True(authoredMatch);

        var error = Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => values.Contains(e.AttackerId)));

        Assert.Contains("Contains", error.Message, StringComparison.Ordinal);
        Assert.Contains("ToArray", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DefaultCases))]
    public void Default_membership_pipelines_keep_interpreted_and_compiled_parity(string kind)
    {
        var values = LinqContainsCases.Forward(kind, new HashSet<string>(StringComparer.Ordinal) { "alice" });

        AssertParity(e => values.Contains(e.AttackerId));
    }

    [Theory]
    [InlineData("distinct-default")]
    [InlineData("distinct-custom")]
    [InlineData("union-default")]
    [InlineData("union-custom")]
    [InlineData("where")]
    [InlineData("select")]
    [InlineData("skip")]
    [InlineData("take")]
    [InlineData("cast")]
    [InlineData("of-type")]
    [InlineData("empty-default")]
    [InlineData("unknown-count-default")]
    [InlineData("shuffle-set-take-all")]
    [InlineData("snapshot")]
    public void Enumeration_membership_does_not_probe_underlying_collection_dispatch(string kind)
    {
        IEnumerable<string> source = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alice", "other" };
        IEnumerable<string> values = kind switch
        {
            "distinct-default" => source.Distinct(StringComparer.Ordinal),
            "distinct-custom" => source.Distinct(StringComparer.OrdinalIgnoreCase),
            "union-default" => source.Union(["ALICE"], StringComparer.Ordinal),
            "union-custom" => source.Union(["ALICE"], StringComparer.OrdinalIgnoreCase),
            "where" => source.Where(x => x.Length > 0),
            "select" => source.Select(x => x),
            "skip" => source.Skip(1),
            "take" => source.Take(1),
            "cast" => new System.Collections.ArrayList { "alice" }.Cast<string>(),
            "of-type" => new System.Collections.ArrayList { "alice", 1 }.OfType<string>(),
            "empty-default" => new HashSet<string>(StringComparer.OrdinalIgnoreCase).DefaultIfEmpty("alice"),
            "unknown-count-default" => source.Where(x => x.Length > 0).DefaultIfEmpty("empty"),
            "shuffle-set-take-all" => source.Shuffle().Take(10),
            "snapshot" => source.Reverse().ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        AssertParity(e => values.Contains(e.AttackerId));
    }

    [Theory]
    [InlineData("range")]
    [InlineData("repeat")]
    [InlineData("list-slice")]
    public void Framework_iterator_collection_implementations_remain_supported(string kind)
    {
        IEnumerable<int> values = kind switch
        {
            "range" => Enumerable.Range(1, 3),
            "repeat" => Enumerable.Repeat(2, 3),
            "list-slice" => new List<int> { 0, 1, 2, 3 }.Skip(1).Take(2),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        AssertParity(e => values.Contains(e.Damage));
    }

    internal static void AssertParity(Expression<Func<AttackTestEvent, bool>> predicate)
    {
        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        Assert.Equal(QueryFilterKind.In, filter.Kind);
        var reader = new MemberValueReader();
        var authored = predicate.Compile();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var name in new[] { "alice", "ALICE", "other", "tail", "head", "start", "end", "empty", "missing" })
        {
            foreach (var damage in new[] { 0, 1, 2, 3, 4 })
            {
                var value = new AttackTestEvent(name, "target", damage, 1);
                Assert.Equal(authored(value), QueryFilterEvaluator.Evaluate(filter, value, reader));
                Assert.Equal(authored(value), compiled(value));
            }
        }
    }
}

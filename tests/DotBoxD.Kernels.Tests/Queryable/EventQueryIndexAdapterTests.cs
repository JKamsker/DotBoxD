using DotBoxD.Plugins.Indexing;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Integration;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Translation;
using HostOperator = DotBoxD.Plugins.IndexPredicateOperator;

namespace DotBoxD.Kernels.Tests.Queryable;

/// <summary>
/// Covers <see cref="EventQueryIndexAdapter"/>: translating a queryable <see cref="EventQueryPlan"/> into the
/// framework <c>DotBoxD.Plugins.IndexedPredicate</c> shape, including the operator/value-kind mappings and an
/// end-to-end check that the produced predicates are consumable by the host <see cref="EventIndexMatcher{TEvent}"/>.
/// </summary>
public sealed class EventQueryIndexAdapterTests
{
    // Marked with the framework's [EventIndexKey] so adapter output can be fed straight into the host matcher.
    private sealed record IndexedAttack(
        [property: EventIndexKey] string AttackerId,
        [property: EventIndexKey] int Damage,
        int AttackerLevel);

    private static IndexedPredicate Predicate(QueryComparisonOperator op, QueryValue value)
        => new() { Path = "F", Operator = op, Value = value };

    [Fact]
    public void ToIndexedPredicates_maps_equality_and_range_from_a_plan()
    {
        var plan = EventQueryPlanner.Plan(
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
                e => e.AttackerId == "player-1" && e.Damage >= 5));

        var predicates = EventQueryIndexAdapter.ToIndexedPredicates(plan);

        Assert.Equal(2, predicates.Count);

        var attacker = predicates.Single(p => p.Path == "AttackerId");
        Assert.Equal(HostOperator.Equals, attacker.Operator);
        Assert.Equal("player-1", attacker.Value);
        Assert.Equal("string", attacker.ValueType);

        var damage = predicates.Single(p => p.Path == "Damage");
        Assert.Equal(HostOperator.GreaterThanOrEqual, damage.Operator);
        Assert.Equal(5L, damage.Value);
        Assert.Equal("long", damage.ValueType);
    }

    [Theory]
    [InlineData(QueryComparisonOperator.Equal, HostOperator.Equals)]
    [InlineData(QueryComparisonOperator.NotEqual, HostOperator.NotEquals)]
    [InlineData(QueryComparisonOperator.GreaterThan, HostOperator.GreaterThan)]
    [InlineData(QueryComparisonOperator.GreaterThanOrEqual, HostOperator.GreaterThanOrEqual)]
    [InlineData(QueryComparisonOperator.LessThan, HostOperator.LessThan)]
    [InlineData(QueryComparisonOperator.LessThanOrEqual, HostOperator.LessThanOrEqual)]
    public void ToIndexedPredicate_maps_each_indexable_operator(QueryComparisonOperator op, HostOperator expected)
    {
        var result = EventQueryIndexAdapter.ToIndexedPredicate(Predicate(op, QueryValue.FromInteger(5)));

        Assert.Equal(expected, result.Operator);
    }

    [Fact]
    public void ToIndexedPredicate_maps_value_kinds_to_wire_tokens()
    {
        var boolean = EventQueryIndexAdapter.ToIndexedPredicate(Predicate(QueryComparisonOperator.Equal, QueryValue.FromBoolean(true)));
        Assert.Equal(true, boolean.Value);
        Assert.Equal("bool", boolean.ValueType);

        var integer = EventQueryIndexAdapter.ToIndexedPredicate(Predicate(QueryComparisonOperator.Equal, QueryValue.FromInteger(42)));
        Assert.Equal(42L, integer.Value);
        Assert.Equal("long", integer.ValueType);

        var number = EventQueryIndexAdapter.ToIndexedPredicate(Predicate(QueryComparisonOperator.GreaterThanOrEqual, QueryValue.FromNumber(2.5)));
        Assert.Equal(2.5d, number.Value);
        Assert.Equal("double", number.ValueType);

        var text = EventQueryIndexAdapter.ToIndexedPredicate(Predicate(QueryComparisonOperator.Equal, QueryValue.FromString("x")));
        Assert.Equal("x", text.Value);
        Assert.Equal("string", text.ValueType);
    }

    [Fact]
    public void Floating_point_member_maps_to_the_double_token()
    {
        var doubleField = EventQueryIndexAdapter
            .ToIndexedPredicates(EventQueryPlanner.Plan(
                ExpressionQueryTranslator.TranslateFilter<MetricTestEvent>(e => e.Score >= 1.5)))
            .Single(p => p.Path == "Score");
        Assert.Equal("double", doubleField.ValueType);
        Assert.Equal(1.5d, doubleField.Value);
    }

    [Fact]
    public void Unsigned_member_is_host_index_ineligible_but_equality_routable()
    {
        // ulong is now an exact kind the host index vocabulary (bool/int/long/double/string) cannot carry, so
        // it is excluded from host IndexedPredicates and re-verified residually — but its equality still routes
        // through the dispatcher's own composite index.
        var plan = EventQueryPlanner.Plan(
            ExpressionQueryTranslator.TranslateFilter<UnsignedTestEvent>(e => e.Big == 5UL));

        Assert.DoesNotContain(plan.IndexedPredicates, p => p.Path == "Big");
        Assert.Empty(EventQueryIndexAdapter.ToIndexedPredicates(plan));
        Assert.Contains(plan.RoutingKeys, p => p.Path == "Big");
    }

    [Fact]
    public void ToIndexedPredicate_throws_for_string_match_operators()
        => Assert.Throws<NotSupportedException>(() =>
            EventQueryIndexAdapter.ToIndexedPredicate(
                Predicate(QueryComparisonOperator.StringContains, QueryValue.FromString("orc"))));

    [Fact]
    public void ToIndexedPredicate_throws_for_a_null_bound()
        => Assert.Throws<NotSupportedException>(() =>
            EventQueryIndexAdapter.ToIndexedPredicate(
                Predicate(QueryComparisonOperator.Equal, QueryValue.Null)));

    [Fact]
    public void ToIndexedPredicate_rejects_null_value_with_public_boundary_exception()
        => AssertPublicBoundaryRejection(
            () => EventQueryIndexAdapter.ToIndexedPredicate(
                new IndexedPredicate
                {
                    Path = "Damage",
                    Operator = QueryComparisonOperator.Equal,
                    Value = null!,
                }),
            "Value");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ToIndexedPredicate_rejects_malformed_paths_with_public_boundary_exception(string? path)
        => AssertPublicBoundaryRejection(
            () => EventQueryIndexAdapter.ToIndexedPredicate(
                new IndexedPredicate
                {
                    Path = path!,
                    Operator = QueryComparisonOperator.Equal,
                    Value = QueryValue.FromInteger(5),
                }),
            "Path");

    [Fact]
    public void ToIndexedPredicates_rejects_null_collection_entries_as_collection_boundary()
        => AssertPublicBoundaryRejection(
            () => EventQueryIndexAdapter.ToIndexedPredicates(
                [
                    Predicate(QueryComparisonOperator.Equal, QueryValue.FromInteger(5)),
                    null!,
                ]),
            "predicates");

    [Fact]
    public void Adapted_predicates_feed_the_host_matcher_and_prefilter_correctly()
    {
        var plan = EventQueryPlanner.Plan(
            ExpressionQueryTranslator.TranslateFilter<IndexedAttack>(
                e => e.AttackerId == "orc-7" && e.Damage >= 10));

        var matcher = EventIndexMatcher<IndexedAttack>.Create(EventQueryIndexAdapter.ToIndexedPredicates(plan));

        Assert.True(matcher.HasIndex);
        Assert.Equal(2, matcher.HonoredPredicates.Count);                       // both paths are [EventIndexKey]
        Assert.True(matcher.CouldMatch(new IndexedAttack("orc-7", 12, 1)));     // satisfies both (long bound vs int field)
        Assert.False(matcher.CouldMatch(new IndexedAttack("orc-7", 3, 1)));     // below the range bound
        Assert.False(matcher.CouldMatch(new IndexedAttack("goblin", 12, 1)));   // wrong equality key
    }

    [Fact]
    public void Predicates_on_unindexed_fields_are_dropped_by_the_host_matcher()
    {
        var plan = EventQueryPlanner.Plan(
            ExpressionQueryTranslator.TranslateFilter<IndexedAttack>(e => e.AttackerLevel >= 5));

        // The adapter still produces a predicate (it is index-eligible in the portable plan)...
        Assert.Single(EventQueryIndexAdapter.ToIndexedPredicates(plan));

        // ...but the host indexes only [EventIndexKey] fields, so the matcher honors none of it and the
        // subscription stays on the broad pipeline. This is the [EventIndexKey] gating that distinguishes
        // the host index from the queryable dispatcher's self-built composite index.
        var matcher = EventIndexMatcher<IndexedAttack>.Create(EventQueryIndexAdapter.ToIndexedPredicates(plan));
        Assert.False(matcher.HasIndex);
        Assert.Empty(matcher.HonoredPredicates);
    }

    private static void AssertPublicBoundaryRejection(Action action, string expectedParamName)
    {
        var exception = Assert.Throws<ArgumentException>(action);

        Assert.Equal(expectedParamName, exception.ParamName);
    }
}

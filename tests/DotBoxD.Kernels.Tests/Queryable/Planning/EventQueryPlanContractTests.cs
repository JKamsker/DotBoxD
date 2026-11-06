using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Integration;
using DotBoxD.Queryable.Planning;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryPlanContractTests
{
    [Theory]
    [InlineData(MalformedPlanShape.NullIndexedPredicates, "IndexedPredicates")]
    [InlineData(MalformedPlanShape.NullRoutingKeys, "RoutingKeys")]
    [InlineData(MalformedPlanShape.NullIndexedPredicateElement, "IndexedPredicates")]
    [InlineData(MalformedPlanShape.NullRoutingKeyElement, "RoutingKeys")]
    [InlineData(MalformedPlanShape.UndefinedCoverage, "Coverage")]
    [InlineData(MalformedPlanShape.FullCoverageWithResidual, "ResidualFilter")]
    [InlineData(MalformedPlanShape.PartialCoverageWithoutResidual, "ResidualFilter")]
    [InlineData(MalformedPlanShape.NoCoverageWithoutResidual, "ResidualFilter")]
    public void Direct_plan_initializers_reject_malformed_contract_values(
        MalformedPlanShape shape,
        string expectedParamName)
        => AssertPlanBoundaryRejection(() => _ = CreateMalformedPlan(shape), expectedParamName);

    [Fact]
    public void Direct_valid_plan_initializer_remains_supported()
    {
        var predicate = Predicate("AttackerId");
        var plan = new EventQueryPlan
        {
            IndexedPredicates = [predicate],
            RoutingKeys = [predicate],
            ResidualFilter = null,
            Coverage = IndexCoverage.Full,
        };

        Assert.True(plan.IsRoutable);
        Assert.Single(EventQueryIndexAdapter.ToIndexedPredicates(plan));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Direct_partial_plan_initializer_accepts_coverage_and_residual_in_either_order(bool coverageFirst)
    {
        var predicate = Predicate("AttackerId");
        var residual = ResidualFilter();
        var plan = coverageFirst
            ? new EventQueryPlan
            {
                IndexedPredicates = [predicate],
                RoutingKeys = [predicate],
                Coverage = IndexCoverage.Partial,
                ResidualFilter = residual,
            }
            : new EventQueryPlan
            {
                IndexedPredicates = [predicate],
                RoutingKeys = [predicate],
                ResidualFilter = residual,
                Coverage = IndexCoverage.Partial,
            };

        Assert.Same(residual, plan.ResidualFilter);
        Assert.Equal(IndexCoverage.Partial, plan.Coverage);
    }

    [Theory]
    [InlineData(QueryComparisonOperator.GreaterThan)]
    [InlineData(QueryComparisonOperator.GreaterThanOrEqual)]
    [InlineData(QueryComparisonOperator.LessThan)]
    [InlineData(QueryComparisonOperator.LessThanOrEqual)]
    public void String_and_boolean_ranges_stay_residual_instead_of_claiming_host_coverage(QueryComparisonOperator op)
    {
        // The query comparer orders strings ordinally and never matches a boolean ordering, whereas a host
        // index orders by its own rules; claiming full coverage would let the host index decide these terms.
        foreach (var value in new[] { QueryValue.FromString("B"), QueryValue.FromBoolean(false) })
        {
            var term = QueryFilter.Compare("AttackerId", op, value);

            var plan = EventQueryPlanner.Plan(term);

            Assert.Empty(plan.IndexedPredicates);
            Assert.Empty(plan.RoutingKeys);
            Assert.Same(term, plan.ResidualFilter);
            Assert.Equal(IndexCoverage.None, plan.Coverage);
        }
    }

    [Fact]
    public void String_and_boolean_equality_remain_host_indexed()
    {
        var filter = QueryFilter.And(
        [
            QueryFilter.Compare("AttackerId", QueryComparisonOperator.Equal, QueryValue.FromString("B")),
            QueryFilter.Compare("Critical", QueryComparisonOperator.Equal, QueryValue.FromBoolean(true)),
        ]);

        var plan = EventQueryPlanner.Plan(filter);

        Assert.Equal(2, plan.IndexedPredicates.Count);
        Assert.Null(plan.ResidualFilter);
        Assert.Equal(IndexCoverage.Full, plan.Coverage);
    }

    private static EventQueryPlan CreateMalformedPlan(MalformedPlanShape shape)
    {
        var predicate = Predicate("AttackerId");
        return shape switch
        {
            MalformedPlanShape.NullIndexedPredicates => new EventQueryPlan
            {
                IndexedPredicates = null!,
                RoutingKeys = [predicate],
                ResidualFilter = null,
                Coverage = IndexCoverage.Full,
            },
            MalformedPlanShape.NullRoutingKeys => new EventQueryPlan
            {
                IndexedPredicates = [predicate],
                RoutingKeys = null!,
                ResidualFilter = null,
                Coverage = IndexCoverage.Full,
            },
            MalformedPlanShape.NullIndexedPredicateElement => new EventQueryPlan
            {
                IndexedPredicates = [predicate, null!],
                RoutingKeys = [predicate],
                ResidualFilter = null,
                Coverage = IndexCoverage.Full,
            },
            MalformedPlanShape.NullRoutingKeyElement => new EventQueryPlan
            {
                IndexedPredicates = [predicate],
                RoutingKeys = [predicate, null!],
                ResidualFilter = null,
                Coverage = IndexCoverage.Full,
            },
            MalformedPlanShape.UndefinedCoverage => new EventQueryPlan
            {
                IndexedPredicates = [],
                RoutingKeys = [],
                ResidualFilter = null,
                Coverage = (IndexCoverage)99,
            },
            MalformedPlanShape.FullCoverageWithResidual => new EventQueryPlan
            {
                IndexedPredicates = [predicate],
                RoutingKeys = [predicate],
                ResidualFilter = ResidualFilter(),
                Coverage = IndexCoverage.Full,
            },
            MalformedPlanShape.PartialCoverageWithoutResidual => new EventQueryPlan
            {
                IndexedPredicates = [predicate],
                RoutingKeys = [predicate],
                ResidualFilter = null,
                Coverage = IndexCoverage.Partial,
            },
            MalformedPlanShape.NoCoverageWithoutResidual => new EventQueryPlan
            {
                IndexedPredicates = [],
                RoutingKeys = [],
                ResidualFilter = null,
                Coverage = IndexCoverage.None,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    private static IndexedPredicate Predicate(string path) => new()
    {
        Path = path,
        Operator = QueryComparisonOperator.Equal,
        Value = QueryValue.FromString("player-1"),
    };

    private static QueryFilter ResidualFilter()
        => QueryFilter.Compare("Damage", QueryComparisonOperator.GreaterThan, QueryValue.FromInteger(5));

    private static void AssertPlanBoundaryRejection(Action action, string expectedParamName)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(action);

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    public enum MalformedPlanShape
    {
        NullIndexedPredicates,
        NullRoutingKeys,
        NullIndexedPredicateElement,
        NullRoutingKeyElement,
        UndefinedCoverage,
        FullCoverageWithResidual,
        PartialCoverageWithoutResidual,
        NoCoverageWithoutResidual,
    }
}

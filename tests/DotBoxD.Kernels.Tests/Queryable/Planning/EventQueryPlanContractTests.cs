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
            Coverage = IndexCoverage.Full,
        };

        Assert.True(plan.IsRoutable);
        Assert.Single(EventQueryIndexAdapter.ToIndexedPredicates(plan));
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
                Coverage = IndexCoverage.Full,
            },
            MalformedPlanShape.NullRoutingKeys => new EventQueryPlan
            {
                IndexedPredicates = [predicate],
                RoutingKeys = null!,
                Coverage = IndexCoverage.Full,
            },
            MalformedPlanShape.NullIndexedPredicateElement => new EventQueryPlan
            {
                IndexedPredicates = [predicate, null!],
                RoutingKeys = [predicate],
                Coverage = IndexCoverage.Full,
            },
            MalformedPlanShape.NullRoutingKeyElement => new EventQueryPlan
            {
                IndexedPredicates = [predicate],
                RoutingKeys = [predicate, null!],
                Coverage = IndexCoverage.Full,
            },
            MalformedPlanShape.UndefinedCoverage => new EventQueryPlan
            {
                IndexedPredicates = [],
                RoutingKeys = [],
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
                Coverage = IndexCoverage.Partial,
            },
            MalformedPlanShape.NoCoverageWithoutResidual => new EventQueryPlan
            {
                IndexedPredicates = [],
                RoutingKeys = [],
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

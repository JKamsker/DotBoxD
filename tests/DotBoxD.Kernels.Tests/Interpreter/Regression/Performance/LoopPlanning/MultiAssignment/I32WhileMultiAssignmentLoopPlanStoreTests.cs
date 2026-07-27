namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.MultiAssignment;

using static MultiAssignmentLoopPlanTestSupport;

public sealed class I32WhileMultiAssignmentLoopPlanStoreTests
{
    [Fact]
    public void Cached_plan_requires_condition_and_every_raw_body_input_but_not_write_only_targets()
    {
        var statement = While(
            LessThan(Variable("counter"), Variable("limit")),
            Assign(
                "counter",
                Add(Variable("counter"), Variable("firstSource"))),
            Assign(
                "writeOnly",
                Add(Variable("secondSource"), Variable("counter"))));
        var setup = CreateFunction(
            [statement],
            "counter",
            "limit",
            "firstSource",
            "secondSource",
            "writeOnly");
        var plan = CreateWhilePlan(statement, CreateFullyAssignedFrame(setup));
        ref var plans = ref setup.Layout.LoopPlans;
        plans.CacheI32WhilePlan(plan);

        var valid = CreateFrame(setup, "counter", "limit", "firstSource", "secondSource");
        Assert.False(valid.IsSlotAssigned(valid.GetSlot("writeOnly")));
        Assert.True(plans.TryGetI32WhilePlan(statement, valid, out var cached));
        Assert.Same(plan, cached);

        Assert.False(TryGetWithout(setup, statement, "counter"));
        Assert.False(TryGetWithout(setup, statement, "limit"));
        Assert.False(TryGetWithout(setup, statement, "firstSource"));
        Assert.False(TryGetWithout(setup, statement, "secondSource"));
    }

    [Fact]
    public void Structurally_equal_statement_references_are_admitted_and_cached_independently()
    {
        var first = While(
            LessThan(Variable("counter"), Variable("limit")),
            Assign("counter", Add(Variable("counter"), Literal(1))),
            Assign("writeOnly", Add(Variable("source"), Literal(2))));
        var second = first with { };
        var setup = CreateFunction(
            [first, second],
            "counter",
            "limit",
            "source",
            "writeOnly");
        var admissionFrame = CreateFullyAssignedFrame(setup);
        var firstPlan = CreateWhilePlan(first, admissionFrame);
        var secondPlan = CreateWhilePlan(second, admissionFrame);

        Assert.Equal(first, second);
        Assert.NotSame(first, second);
        ref var plans = ref setup.Layout.LoopPlans;
        Assert.False(plans.ShouldCacheI32WhilePlan(first));
        Assert.False(plans.ShouldCacheI32WhilePlan(second));
        Assert.True(plans.ShouldCacheI32WhilePlan(first));
        plans.CacheI32WhilePlan(firstPlan);
        Assert.True(plans.ShouldCacheI32WhilePlan(second));
        plans.CacheI32WhilePlan(secondPlan);

        var lookupFrame = CreateFrame(setup, "counter", "limit", "source");
        Assert.True(plans.TryGetI32WhilePlan(first, lookupFrame, out var firstCached));
        Assert.True(plans.TryGetI32WhilePlan(second, lookupFrame, out var secondCached));
        Assert.Same(firstPlan, firstCached);
        Assert.Same(secondPlan, secondCached);

        var equalButUncached = first with { };
        Assert.Equal(first, equalButUncached);
        Assert.NotSame(first, equalButUncached);
        Assert.False(plans.TryGetI32WhilePlan(equalButUncached, lookupFrame, out _));
    }

    [Fact]
    public async Task Concurrent_admission_publishes_one_complete_multi_assignment_plan()
    {
        var statement = While(
            LessThan(Variable("counter"), Variable("limit")),
            Assign("counter", Add(Variable("counter"), Literal(1))),
            Assign("writeOnly", Add(Variable("source"), Literal(2))));
        var setup = CreateFunction(
            [statement],
            "counter",
            "limit",
            "source",
            "writeOnly");
        var admissionFrame = CreateFullyAssignedFrame(setup);
        var candidates = Enumerable.Range(0, 8)
            .Select(_ => CreateWhilePlan(statement, admissionFrame))
            .ToArray();
        using var start = new ManualResetEventSlim();
        var admissions = candidates.Select(candidate => Task.Run(() =>
        {
            start.Wait();
            ref var plans = ref setup.Layout.LoopPlans;
            if (plans.ShouldCacheI32WhilePlan(statement))
            {
                plans.CacheI32WhilePlan(candidate);
            }
        })).ToArray();

        start.Set();
        await Task.WhenAll(admissions);

        var lookupFrame = CreateFrame(setup, "counter", "limit", "source");
        ref var publishedPlans = ref setup.Layout.LoopPlans;
        Assert.True(publishedPlans.TryGetI32WhilePlan(statement, lookupFrame, out var published));
        Assert.Contains(candidates, candidate => ReferenceEquals(candidate, published));
        Assert.Equal(2, published.MultipleAssignments.Length);
        Assert.False(publishedPlans.ShouldCacheI32WhilePlan(statement));
    }

    private static bool TryGetWithout(
        FunctionSetup setup,
        WhileStatement statement,
        string missing)
    {
        var assigned = setup.Locals.Where(local => local != missing && local != "writeOnly").ToArray();
        var frame = CreateFrame(setup, assigned);
        return setup.Layout.LoopPlans.TryGetI32WhilePlan(statement, frame, out _);
    }
}

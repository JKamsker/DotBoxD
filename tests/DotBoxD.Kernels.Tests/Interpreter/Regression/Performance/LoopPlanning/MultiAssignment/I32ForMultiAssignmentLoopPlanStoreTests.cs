namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.MultiAssignment;

using static MultiAssignmentLoopPlanTestSupport;

public sealed class I32ForMultiAssignmentLoopPlanStoreTests
{
    [Fact]
    public void Cached_plan_requires_every_raw_body_input_but_not_write_only_targets()
    {
        var statement = ForRange(
            Assign(
                "accumulator",
                Add(Variable("accumulator"), Variable("firstSource"))),
            Assign(
                "writeOnly",
                Add(Variable("secondSource"), Variable("i"))));
        var setup = CreateFunction(
            [statement],
            "accumulator",
            "firstSource",
            "secondSource",
            "writeOnly");
        var plan = CreateForPlan(statement, CreateFullyAssignedFrame(setup));
        ref var plans = ref setup.Layout.LoopPlans;
        plans.CacheI32ForRangePlan(plan);

        var valid = CreateFrame(setup, "accumulator", "firstSource", "secondSource");
        var loopSlot = valid.GetSlot(statement.LocalName);
        Assert.False(valid.IsSlotAssigned(valid.GetSlot("writeOnly")));
        Assert.True(plans.TryGetI32ForRangePlan(statement, valid, loopSlot, out var cached));
        Assert.Same(plan, cached);

        Assert.False(TryGetWithout(setup, statement, "accumulator"));
        Assert.False(TryGetWithout(setup, statement, "firstSource"));
        Assert.False(TryGetWithout(setup, statement, "secondSource"));
    }

    [Fact]
    public void Three_assignment_plan_requires_the_trailing_source_and_retains_the_complete_body()
    {
        var statement = ForRange(
            Assign("total", Add(Variable("total"), Variable("firstSource"))),
            Assign("intermediate", Add(Variable("total"), Variable("secondSource"))),
            Assign("writeOnly", Add(Variable("intermediate"), Variable("trailingSource"))));
        var setup = CreateFunction(
            [statement],
            "total",
            "firstSource",
            "intermediate",
            "secondSource",
            "trailingSource",
            "writeOnly");
        var plan = CreateForPlan(statement, CreateFullyAssignedFrame(setup));
        ref var plans = ref setup.Layout.LoopPlans;
        plans.CacheI32ForRangePlan(plan);

        var valid = CreateFrame(
            setup,
            "total",
            "firstSource",
            "intermediate",
            "secondSource",
            "trailingSource");
        var loopSlot = valid.GetSlot(statement.LocalName);
        Assert.True(plans.TryGetI32ForRangePlan(statement, valid, loopSlot, out var cached));
        Assert.Same(plan, cached);
        Assert.Equal(3, cached.MultipleAssignments.Length);

        var missingTrailing = CreateFrame(
            setup,
            "total",
            "firstSource",
            "intermediate",
            "secondSource");
        Assert.False(plans.TryGetI32ForRangePlan(
            statement,
            missingTrailing,
            missingTrailing.GetSlot(statement.LocalName),
            out _));
    }

    [Fact]
    public void Cached_plan_preserves_loop_and_explicit_prewrite_exemptions()
    {
        var statement = ForRange(
            Assign("target", Add(Variable("i"), Variable("outer"))),
            Assign("writeOnly", Add(Variable("i"), Literal(1))));
        var setup = CreateFunction([statement], "outer", "target", "writeOnly");
        var plan = CreateForPlan(statement, CreateFullyAssignedFrame(setup));
        ref var plans = ref setup.Layout.LoopPlans;
        plans.CacheI32ForRangePlan(plan);

        var outerAssigned = CreateFrame(setup, "outer");
        var loopSlot = outerAssigned.GetSlot(statement.LocalName);
        Assert.True(plans.TryGetI32ForRangePlan(
            statement,
            outerAssigned,
            loopSlot,
            out var cached));
        Assert.Same(plan, cached);

        var bothPrewrittenByRunner = CreateFrame(setup);
        var outerSlot = bothPrewrittenByRunner.GetSlot("outer");
        Assert.False(plans.TryGetI32ForRangePlan(
            statement,
            bothPrewrittenByRunner,
            loopSlot,
            out _));
        Assert.True(plans.TryGetI32ForRangePlan(
            statement,
            bothPrewrittenByRunner,
            loopSlot,
            outerSlot,
            out cached));
        Assert.Same(plan, cached);
        Assert.False(plans.TryGetI32ForRangePlan(
            statement,
            bothPrewrittenByRunner,
            loopSlot,
            bothPrewrittenByRunner.GetSlot("writeOnly"),
            out _));
    }

    [Fact]
    public void Structurally_equal_statement_references_are_admitted_and_cached_independently()
    {
        var first = ForRange(
            Assign("target", Add(Variable("source"), Literal(1))),
            Assign("writeOnly", Add(Variable("source"), Literal(2))));
        var second = first with { };
        var setup = CreateFunction([first, second], "source", "target", "writeOnly");
        var admissionFrame = CreateFullyAssignedFrame(setup);
        var firstPlan = CreateForPlan(first, admissionFrame);
        var secondPlan = CreateForPlan(second, admissionFrame);

        Assert.Equal(first, second);
        Assert.NotSame(first, second);
        ref var plans = ref setup.Layout.LoopPlans;
        Assert.False(plans.ShouldCacheI32ForRangePlan(first));
        Assert.False(plans.ShouldCacheI32ForRangePlan(second));
        Assert.True(plans.ShouldCacheI32ForRangePlan(first));
        plans.CacheI32ForRangePlan(firstPlan);
        Assert.True(plans.ShouldCacheI32ForRangePlan(second));
        plans.CacheI32ForRangePlan(secondPlan);

        var lookupFrame = CreateFrame(setup, "source");
        var loopSlot = lookupFrame.GetSlot(first.LocalName);
        Assert.True(plans.TryGetI32ForRangePlan(first, lookupFrame, loopSlot, out var firstCached));
        Assert.True(plans.TryGetI32ForRangePlan(second, lookupFrame, loopSlot, out var secondCached));
        Assert.Same(firstPlan, firstCached);
        Assert.Same(secondPlan, secondCached);

        var equalButUncached = first with { };
        Assert.Equal(first, equalButUncached);
        Assert.NotSame(first, equalButUncached);
        Assert.False(plans.TryGetI32ForRangePlan(equalButUncached, lookupFrame, loopSlot, out _));
    }

    [Fact]
    public async Task Concurrent_admission_publishes_one_complete_multi_assignment_plan()
    {
        var statement = ForRange(
            Assign("target", Add(Variable("source"), Literal(1))),
            Assign("writeOnly", Add(Variable("source"), Literal(2))));
        var setup = CreateFunction([statement], "source", "target", "writeOnly");
        var admissionFrame = CreateFullyAssignedFrame(setup);
        var candidates = Enumerable.Range(0, 8)
            .Select(_ => CreateForPlan(statement, admissionFrame))
            .ToArray();
        using var start = new ManualResetEventSlim();
        var admissions = candidates.Select(candidate => Task.Run(() =>
        {
            start.Wait();
            ref var plans = ref setup.Layout.LoopPlans;
            if (plans.ShouldCacheI32ForRangePlan(statement))
            {
                plans.CacheI32ForRangePlan(candidate);
            }
        })).ToArray();

        start.Set();
        await Task.WhenAll(admissions);

        var lookupFrame = CreateFrame(setup, "source");
        ref var publishedPlans = ref setup.Layout.LoopPlans;
        Assert.True(publishedPlans.TryGetI32ForRangePlan(
            statement,
            lookupFrame,
            lookupFrame.GetSlot(statement.LocalName),
            out var published));
        Assert.Contains(candidates, candidate => ReferenceEquals(candidate, published));
        Assert.Equal(2, published.MultipleAssignments.Length);
        Assert.False(publishedPlans.ShouldCacheI32ForRangePlan(statement));
    }

    private static bool TryGetWithout(
        FunctionSetup setup,
        ForRangeStatement statement,
        string missing)
    {
        var assigned = setup.Locals.Where(local => local != missing && local != "writeOnly").ToArray();
        var frame = CreateFrame(setup, assigned);
        return setup.Layout.LoopPlans.TryGetI32ForRangePlan(
            statement,
            frame,
            frame.GetSlot(statement.LocalName),
            out _);
    }
}

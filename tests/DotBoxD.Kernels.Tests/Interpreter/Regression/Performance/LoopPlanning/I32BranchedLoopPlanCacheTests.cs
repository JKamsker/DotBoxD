using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Interpreter.Internal.Loops;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class I32BranchedLoopPlanCacheTests
{
    [Fact]
    public void Cached_plan_revalidates_both_branches_and_uses_reference_keys()
    {
        var setup = CreateDirectPlan();
        ref var plans = ref setup.Layout.LoopPlans;
        Assert.False(plans.ShouldCacheI32BranchedForRangePlan(setup.Statement));
        Assert.True(plans.ShouldCacheI32BranchedForRangePlan(setup.Statement));
        plans.CacheI32BranchedForRangePlan(setup.Plan);

        Assert.True(plans.TryGetI32BranchedForRangePlan(
            setup.Statement,
            setup.AssignedFrame,
            out var cached));
        Assert.Same(setup.Plan, cached);

        var equalStatement = setup.Statement with { };
        Assert.Equal(setup.Statement, equalStatement);
        Assert.NotSame(setup.Statement, equalStatement);
        Assert.False(plans.TryGetI32BranchedForRangePlan(
            equalStatement,
            setup.AssignedFrame,
            out _));

        AssertCacheMissesWithMissingSlot(setup, "limit");
        AssertCacheMissesWithMissingSlot(setup, "thenValue");
        AssertCacheMissesWithMissingSlot(setup, "elseValue");
    }

    [Fact]
    public void Multi_assignment_branch_is_not_reusable()
    {
        var setup = CreateDirectPlan();
        var single = setup.Plan.Data.Then.SingleAssignment;
        var many = new I32BranchedBranchPlan(
            I32BranchedBranchKind.Many,
            default,
            [single, single],
            setup.Plan.Data.Then.Fuel * 2);
        var data = new I32BranchedPlanData(setup.Plan.Data.Condition, many, setup.Plan.Data.Else);

        Assert.True(setup.Plan.Data.IsReusable);
        Assert.False(data.IsReusable);

        var multiBranch = setup.Branch with { Then = [setup.Branch.Then[0], setup.Branch.Then[0]] };
        Assert.True(I32BranchedLoopPlanner.TryGetKnownTarget(
            multiBranch,
            setup.AssignedFrame,
            out var knownTarget));
        Assert.False(I32BranchedLoopPlanner.TryCreateReusable(
            multiBranch,
            "i",
            setup.AssignedFrame,
            NoCalls.Instance,
            knownTarget,
            out _));
    }

    [Fact]
    public void Reusable_planner_resolves_distinct_then_and_else_targets()
    {
        var setup = CreateDirectPlan();
        Assert.True(I32BranchedLoopPlanner.TryGetKnownTarget(
            setup.Branch,
            setup.AssignedFrame,
            out var knownTarget));
        Assert.True(I32BranchedLoopPlanner.TryCreateReusable(
            setup.Branch,
            "i",
            setup.AssignedFrame,
            NoCalls.Instance,
            knownTarget,
            out var data));

        Assert.Equal(
            setup.AssignedFrame.GetSlot("thenTotal"),
            data.Then.SingleAssignment.TargetSlot);
        Assert.Equal(
            setup.AssignedFrame.GetSlot("elseTotal"),
            data.Else.SingleAssignment.TargetSlot);
    }

    [Fact]
    public async Task Concurrent_admission_publishes_a_reusable_plan()
    {
        var setup = CreateDirectPlan();
        using var start = new ManualResetEventSlim();
        var admissions = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                ref var plans = ref setup.Layout.LoopPlans;
                if (plans.ShouldCacheI32BranchedForRangePlan(setup.Statement))
                {
                    plans.CacheI32BranchedForRangePlan(setup.Plan);
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(admissions);

        ref var publishedPlans = ref setup.Layout.LoopPlans;
        Assert.True(publishedPlans.TryGetI32BranchedForRangePlan(
            setup.Statement,
            setup.AssignedFrame,
            out var published));
        Assert.Same(setup.Plan, published);
    }

    [Fact]
    public void Distinct_statements_are_admitted_and_retrieved_independently()
    {
        var setup = CreateDirectPlan();
        var secondStatement = setup.Statement with { };
        var secondPlan = new I32BranchedLoopPlan(secondStatement, setup.Plan.Data);
        ref var plans = ref setup.Layout.LoopPlans;

        Assert.False(plans.ShouldCacheI32BranchedForRangePlan(setup.Statement));
        Assert.False(plans.ShouldCacheI32BranchedForRangePlan(secondStatement));
        Assert.True(plans.ShouldCacheI32BranchedForRangePlan(setup.Statement));
        plans.CacheI32BranchedForRangePlan(setup.Plan);
        Assert.True(plans.ShouldCacheI32BranchedForRangePlan(secondStatement));
        plans.CacheI32BranchedForRangePlan(secondPlan);

        Assert.True(plans.TryGetI32BranchedForRangePlan(
            setup.Statement,
            setup.AssignedFrame,
            out var firstCached));
        Assert.True(plans.TryGetI32BranchedForRangePlan(
            secondStatement,
            setup.AssignedFrame,
            out var secondCached));
        Assert.Same(setup.Plan, firstCached);
        Assert.Same(secondPlan, secondCached);
    }

    private static void AssertCacheMissesWithMissingSlot(DirectPlanSetup setup, string missingSlot)
    {
        var assigned = new[] { "total", "limit", "thenValue", "elseValue" }
            .Where(name => name != missingSlot)
            .ToArray();
        var frame = CreateFrame(setup.Layout, setup.Function, assigned);

        Assert.False(setup.Layout.LoopPlans.TryGetI32BranchedForRangePlan(
            setup.Statement,
            frame,
            out _));
    }

    private static DirectPlanSetup CreateDirectPlan()
    {
        var span = new SourceSpan(1, 1);
        var condition = new BinaryExpression(Variable("i", span), "<", Variable("limit", span), span);
        var thenExpression = new BinaryExpression(
            Variable("total", span), "+", Variable("thenValue", span), span);
        var elseExpression = new BinaryExpression(
            Variable("total", span), "+", Variable("elseValue", span), span);
        var thenAssignment = new AssignmentStatement("thenTotal", thenExpression, span);
        var elseAssignment = new AssignmentStatement("elseTotal", elseExpression, span);
        var branch = new IfStatement(condition, [thenAssignment], [elseAssignment], span);
        var statement = new ForRangeStatement(
            "i",
            Literal(0, span),
            Variable("limit", span),
            [branch],
            span);
        var function = Function(statement, span);
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            new BindingRegistryBuilder().Build());
        var frame = CreateFrame(layout, function, "total", "limit", "thenValue", "elseValue");
        Assert.True(I32ComparisonPlan.TryCreate(condition, frame, "i", out var conditionPlan));
        Assert.True(I32ExpressionPlan.TryCreate(thenExpression, frame, "i", out var thenPlan));
        Assert.True(I32ExpressionPlan.TryCreate(elseExpression, frame, "i", out var elsePlan));
        var thenBranch = SingleBranch(frame.GetSlot("thenTotal"), thenPlan);
        var elseBranch = SingleBranch(frame.GetSlot("elseTotal"), elsePlan);
        var data = new I32BranchedPlanData(conditionPlan, thenBranch, elseBranch);
        return new DirectPlanSetup(
            layout,
            function,
            statement,
            branch,
            new I32BranchedLoopPlan(statement, data),
            frame);
    }

    private static SandboxFunction Function(ForRangeStatement statement, SourceSpan span)
        => new(
            "main",
            true,
            [],
            SandboxType.I32,
            [
                new AssignmentStatement("total", Literal(0, span), span),
                new AssignmentStatement("thenTotal", Literal(0, span), span),
                new AssignmentStatement("elseTotal", Literal(0, span), span),
                new AssignmentStatement("limit", Literal(1, span), span),
                new AssignmentStatement("thenValue", Literal(2, span), span),
                new AssignmentStatement("elseValue", Literal(3, span), span),
                statement
            ]);

    private static I32BranchedBranchPlan SingleBranch(int targetSlot, I32ExpressionPlan expression)
        => new(
            I32BranchedBranchKind.Single,
            new I32BranchedAssignmentPlan(targetSlot, expression),
            null,
            1 + expression.FuelCost);

    private static InterpreterFrame CreateFrame(
        FunctionFrameLayout layout,
        SandboxFunction function,
        params string[] assignedLocals)
    {
        var frame = InterpreterFrame.Create(layout, function, LocalFunctionArguments.FromArray([]));
        foreach (var local in assignedLocals)
        {
            frame.WriteInt32(local, 1);
        }

        return frame;
    }

    private static VariableExpression Variable(string name, SourceSpan span) => new(name, span);

    private static LiteralExpression Literal(int value, SourceSpan span)
        => new(SandboxValue.FromInt32(value), span);

    private sealed class NoCalls : I32CallEvaluator
    {
        public static NoCalls Instance { get; } = new();

        public bool CanEvaluateInt32Call(CallExpression call) => false;

        public int EvaluateInt32Call(CallExpression call) => throw new NotSupportedException();

        public bool TryCreateInt32CallPlan(
            CallExpression call,
            InterpreterFrame frame,
            string assumedInt32Local,
            out I32ExpressionPlan plan)
        {
            plan = null!;
            return false;
        }
    }

    private sealed record DirectPlanSetup(
        FunctionFrameLayout Layout,
        SandboxFunction Function,
        ForRangeStatement Statement,
        IfStatement Branch,
        I32BranchedLoopPlan Plan,
        InterpreterFrame AssignedFrame);
}

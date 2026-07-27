using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Interpreter.Internal.Loops;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.MultiAssignment;

using static MultiAssignmentLoopPlanTestSupport;

public sealed class MultiAssignmentNonReusablePlanTests
{
    [Fact]
    public void Boxed_i32_body_source_is_plannable_but_never_cached_for_range()
    {
        var setup = CreateForRangeSetup();
        Assert.True(setup.Layout.IsBoxedSlot(setup.Layout.GetSlot("boxedSource")));

        foreach (var source in new[] { 2, 5, 9 })
        {
            var frame = CreateForRangeFrame(setup, source);
            var firstAssignment = Assert.IsType<AssignmentStatement>(setup.Statement.Body[0]);
            Assert.True(I32ExpressionPlan.TryCreate(
                firstAssignment.Value,
                frame,
                setup.Statement.LocalName,
                out var boxedExpression));
            Assert.False(boxedExpression.HasOnlyRawVariables());

            using var context = CreateContext();
            Assert.True(I32ForLoopRunner.TryRun(
                setup.Statement,
                start: 0,
                end: 1,
                frame,
                context,
                Options(),
                NoCalls.Instance));

            Assert.Equal(source + 2, frame.ReadInt32("observed"));
            Assert.Equal(13, context.Budget.FuelUsed);
            Assert.Equal(1, context.Budget.LoopIterations);
            Assert.False(setup.Layout.LoopPlans.TryGetI32ForRangePlan(
                setup.Statement,
                frame,
                frame.GetSlot(setup.Statement.LocalName),
                out _));
        }
    }

    [Fact]
    public void Boxed_i32_condition_source_is_plannable_but_never_cached_for_while()
    {
        var setup = CreateWhileSetup();
        Assert.True(setup.Layout.IsBoxedSlot(setup.Layout.GetSlot("boxedLimit")));

        foreach (var limit in new[] { 1, 2, 3 })
        {
            var frame = CreateWhileFrame(setup, limit);
            Assert.True(I32ComparisonPlan.TryCreate(
                setup.Statement.Condition,
                frame,
                "",
                out var boxedCondition));
            Assert.False(boxedCondition.HasOnlyRawVariables());

            using var context = CreateContext();
            Assert.True(WhileI32ForLoopRunner.TryRun(
                setup.Statement,
                frame,
                context,
                Options(),
                NoCalls.Instance));

            Assert.Equal(limit, frame.ReadInt32("counter"));
            Assert.Equal(limit + 1, frame.ReadInt32("observed"));
            Assert.Equal((16L * limit) + 3, context.Budget.FuelUsed);
            Assert.Equal(limit, context.Budget.LoopIterations);
            Assert.False(setup.Layout.LoopPlans.TryGetI32WhilePlan(
                setup.Statement,
                frame,
                out _));
        }
    }

    [Fact]
    public void Boxed_i32_body_source_reaches_the_while_body_reuse_gate_and_is_never_cached()
    {
        var setup = CreateWhileBodySetup();
        Assert.True(setup.Layout.IsBoxedSlot(setup.Layout.GetSlot("boxedSource")));

        foreach (var source in new[] { 2, 5, 9 })
        {
            var frame = CreateWhileBodyFrame(setup, source);
            Assert.True(I32ComparisonPlan.TryCreate(
                setup.Statement.Condition,
                frame,
                "",
                out var rawCondition));
            Assert.True(rawCondition.HasOnlyRawVariables());
            var secondAssignment = Assert.IsType<AssignmentStatement>(setup.Statement.Body[1]);
            Assert.True(I32ExpressionPlan.TryCreate(
                secondAssignment.Value,
                frame,
                "",
                out var boxedExpression));
            Assert.False(boxedExpression.HasOnlyRawVariables());

            using var context = CreateContext();
            Assert.True(WhileI32ForLoopRunner.TryRun(
                setup.Statement,
                frame,
                context,
                Options(),
                NoCalls.Instance));

            Assert.Equal(1, frame.ReadInt32("counter"));
            Assert.Equal(source + 1, frame.ReadInt32("observed"));
            Assert.Equal(19, context.Budget.FuelUsed);
            Assert.Equal(1, context.Budget.LoopIterations);
            Assert.False(setup.Layout.LoopPlans.TryGetI32WhilePlan(
                setup.Statement,
                frame,
                out _));
        }
    }

    private static ForRangeSetup CreateForRangeSetup()
    {
        var statement = ForRange(
            Assign("target", Add(Literal(1), Variable("boxedSource"))),
            Assign("observed", Add(Variable("target"), Literal(1))));
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I32,
            [
                Assign("boxedSource", Literal(0)),
                Assign("boxedSource", StringLiteral("boxed")),
                Assign("target", Literal(0)),
                Assign("observed", Literal(0)),
                statement
            ]);
        return new ForRangeSetup(BuildLayout(function), function, statement);
    }

    private static WhileSetup CreateWhileSetup()
    {
        var statement = While(
            LessThan(Variable("counter"), Variable("boxedLimit")),
            Assign("counter", Add(Variable("counter"), Literal(1))),
            Assign("observed", Add(Variable("counter"), Literal(1))));
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I32,
            [
                Assign("boxedLimit", Literal(0)),
                Assign("boxedLimit", StringLiteral("boxed")),
                Assign("counter", Literal(0)),
                Assign("observed", Literal(0)),
                statement
            ]);
        return new WhileSetup(BuildLayout(function), function, statement);
    }

    private static WhileSetup CreateWhileBodySetup()
    {
        var statement = While(
            LessThan(Variable("counter"), Variable("limit")),
            Assign("counter", Add(Variable("counter"), Literal(1))),
            Assign("observed", Add(Variable("counter"), Variable("boxedSource"))));
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I32,
            [
                Assign("boxedSource", Literal(0)),
                Assign("boxedSource", StringLiteral("boxed")),
                Assign("counter", Literal(0)),
                Assign("limit", Literal(1)),
                Assign("observed", Literal(0)),
                statement
            ]);
        return new WhileSetup(BuildLayout(function), function, statement);
    }

    private static InterpreterFrame CreateForRangeFrame(ForRangeSetup setup, int source)
    {
        var frame = CreateFrame(setup.Layout, setup.Function);
        frame.Write("boxedSource", SandboxValue.FromInt32(source));
        frame.WriteInt32("target", 0);
        frame.WriteInt32("observed", 0);
        return frame;
    }

    private static InterpreterFrame CreateWhileFrame(WhileSetup setup, int limit)
    {
        var frame = CreateFrame(setup.Layout, setup.Function);
        frame.Write("boxedLimit", SandboxValue.FromInt32(limit));
        frame.WriteInt32("counter", 0);
        frame.WriteInt32("observed", 0);
        return frame;
    }

    private static InterpreterFrame CreateWhileBodyFrame(WhileSetup setup, int source)
    {
        var frame = CreateFrame(setup.Layout, setup.Function);
        frame.Write("boxedSource", SandboxValue.FromInt32(source));
        frame.WriteInt32("counter", 0);
        frame.WriteInt32("limit", 1);
        frame.WriteInt32("observed", 0);
        return frame;
    }

    private static InterpreterFrame CreateFrame(FunctionFrameLayout layout, SandboxFunction function)
        => InterpreterFrame.Create(layout, function, LocalFunctionArguments.FromArray([]));

    private static FunctionFrameLayout BuildLayout(SandboxFunction function)
        => FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            new BindingRegistryBuilder().Build());

    private static LiteralExpression StringLiteral(string value)
        => new(SandboxValue.FromString(value), Span);

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

    private static SandboxContext CreateContext()
    {
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithMaxLoopIterations(100)
            .Build();
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

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

    private sealed record ForRangeSetup(
        FunctionFrameLayout Layout,
        SandboxFunction Function,
        ForRangeStatement Statement);

    private sealed record WhileSetup(
        FunctionFrameLayout Layout,
        SandboxFunction Function,
        WhileStatement Statement);
}

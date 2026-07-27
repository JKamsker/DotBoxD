using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Interpreter.Internal.Loops;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class I64MultiAssignmentForLoopPlanTests
{
    [Fact]
    public void Cached_plan_distinguishes_entry_reads_from_earlier_body_writes()
    {
        var setup = CreateSourceOrderedSetup();
        ref var plans = ref setup.Layout.LoopPlans;
        plans.CacheI64ForRangePlan(setup.Plan);

        var valid = CreateFrame(setup, "source", "trailing");
        Assert.False(valid.IsSlotAssigned(valid.GetSlot("intermediate")));
        Assert.False(valid.IsSlotAssigned(valid.GetSlot("result")));
        Assert.True(plans.TryGetI64ForRangePlan(setup.Statement, valid, out var cached));
        Assert.Same(setup.Plan, cached);
        Assert.Equal(3, cached.MultipleAssignments.Length);

        Assert.False(TryGetWithAssigned(setup, "trailing"));
        Assert.False(TryGetWithAssigned(setup, "source"));
    }

    [Fact]
    public void Self_read_before_first_write_remains_an_entry_requirement()
    {
        var setup = CreateSelfReadSetup();
        ref var plans = ref setup.Layout.LoopPlans;
        plans.CacheI64ForRangePlan(setup.Plan);

        Assert.False(plans.TryGetI64ForRangePlan(
            setup.Statement,
            CreateFrame(setup),
            out _));
        Assert.True(plans.TryGetI64ForRangePlan(
            setup.Statement,
            CreateFrame(setup, "intermediate"),
            out _));
    }

    [Fact]
    public void Unsupported_body_does_not_advance_plan_admission()
    {
        var span = new SourceSpan(1, 1);
        var supported = new AssignmentStatement(
            "total",
            Add(Variable("total", span), Literal(1, span), span),
            span);
        var unsupported = new ExpressionStatement(Literal(2, span), span);
        var statement = ForRange([supported, unsupported], span);
        var function = Function(statement, "total");
        var layout = Layout(function);
        var frame = InterpreterFrame.Create(
            layout,
            function,
            LocalFunctionArguments.FromArray([]));
        frame.WriteRawInt64Slot(frame.GetSlot("total"), 0);
        using var context = CreateContext();

        Assert.False(I64MultiAssignmentForLoopRunner.TryRun(
            statement, 0, 1, frame, context));
        Assert.False(I64MultiAssignmentForLoopRunner.TryRun(
            statement, 0, 1, frame, context));
        Assert.Equal(0, context.Budget.FuelUsed);
        Assert.Equal(0, context.Budget.LoopIterations);

        ref var plans = ref layout.LoopPlans;
        Assert.False(plans.ShouldCacheI64ForRangePlan(statement));
        Assert.True(plans.ShouldCacheI64ForRangePlan(statement));
    }

    [Fact]
    public async Task Concurrent_admission_publishes_one_complete_immutable_body()
    {
        var setup = CreateSourceOrderedSetup();
        var candidates = Enumerable.Range(0, 8)
            .Select(_ => CreatePlan(setup.Statement, setup.AdmissionFrame))
            .ToArray();
        using var start = new ManualResetEventSlim();
        var admissions = candidates.Select(candidate => Task.Run(() =>
        {
            start.Wait();
            ref var plans = ref setup.Layout.LoopPlans;
            if (plans.ShouldCacheI64ForRangePlan(setup.Statement))
            {
                plans.CacheI64ForRangePlan(candidate);
            }
        })).ToArray();

        start.Set();
        await Task.WhenAll(admissions);

        var frame = CreateFrame(setup, "source", "trailing");
        ref var publishedPlans = ref setup.Layout.LoopPlans;
        Assert.True(publishedPlans.TryGetI64ForRangePlan(
            setup.Statement,
            frame,
            out var published));
        Assert.Contains(candidates, candidate => ReferenceEquals(candidate, published));
        Assert.Equal(3, published.MultipleAssignments.Length);
        Assert.False(publishedPlans.ShouldCacheI64ForRangePlan(setup.Statement));
    }

    private static Setup CreateSourceOrderedSetup()
    {
        var span = new SourceSpan(1, 1);
        var statement = ForRange(
            [
                Assign("intermediate", Add(Variable("source", span), Literal(1, span), span), span),
                Assign("result", Add(Variable("intermediate", span), Variable("trailing", span), span), span),
                Assign("writeOnly", Literal(5, span), span)
            ],
            span);
        var function = Function(
            statement,
            "source",
            "trailing",
            "intermediate",
            "result",
            "writeOnly");
        var layout = Layout(function);
        var setup = new Setup(layout, function, statement, null!, null!);
        var admissionFrame = CreateFrame(
            setup,
            "source",
            "trailing",
            "intermediate",
            "result",
            "writeOnly");
        return setup with
        {
            Plan = CreatePlan(statement, admissionFrame),
            AdmissionFrame = admissionFrame
        };
    }

    private static Setup CreateSelfReadSetup()
    {
        var span = new SourceSpan(1, 1);
        var statement = ForRange(
            [
                Assign("intermediate", Add(Variable("intermediate", span), Literal(1, span), span), span),
                Assign("result", Literal(2, span), span)
            ],
            span);
        var function = Function(statement, "intermediate", "result");
        var layout = Layout(function);
        var setup = new Setup(layout, function, statement, null!, null!);
        var admissionFrame = CreateFrame(setup, "intermediate", "result");
        return setup with
        {
            Plan = CreatePlan(statement, admissionFrame),
            AdmissionFrame = admissionFrame
        };
    }

    private static I64ForLoopPlan CreatePlan(
        ForRangeStatement statement,
        InterpreterFrame frame)
    {
        var assignments = new I64LoopAssignmentPlan[statement.Body.Count];
        long fuel = 5;
        for (var i = 0; i < statement.Body.Count; i++)
        {
            var assignment = Assert.IsType<AssignmentStatement>(statement.Body[i]);
            Assert.True(I64ExpressionPlan.TryCreate(
                assignment.Value,
                frame,
                out var expression));
            assignments[i] = new I64LoopAssignmentPlan(
                frame.GetSlot(assignment.Name),
                expression);
            fuel += 1 + expression.FuelCost;
        }

        return new I64ForLoopPlan(statement, assignments, fuel);
    }

    private static bool TryGetWithAssigned(Setup setup, params string[] assigned)
        => setup.Layout.LoopPlans.TryGetI64ForRangePlan(
            setup.Statement,
            CreateFrame(setup, assigned),
            out _);

    private static InterpreterFrame CreateFrame(Setup setup, params string[] assigned)
    {
        var frame = InterpreterFrame.Create(
            setup.Layout,
            setup.Function,
            LocalFunctionArguments.FromArray([]));
        foreach (var name in assigned)
        {
            frame.WriteRawInt64Slot(frame.GetSlot(name), 1);
        }

        return frame;
    }

    private static SandboxFunction Function(
        ForRangeStatement statement,
        params string[] locals)
    {
        var span = statement.Span;
        var declarations = locals
            .Select(name => (Statement)Assign(name, Literal(0, span), span))
            .Append(statement)
            .ToArray();
        return new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I64,
            declarations);
    }

    private static FunctionFrameLayout Layout(SandboxFunction function)
        => FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            new BindingRegistryBuilder().Build());

    private static ForRangeStatement ForRange(
        IReadOnlyList<Statement> body,
        SourceSpan span)
        => new("i", LiteralI32(0, span), LiteralI32(1, span), body, span);

    private static AssignmentStatement Assign(
        string name,
        Expression expression,
        SourceSpan span)
        => new(name, expression, span);

    private static BinaryExpression Add(
        Expression left,
        Expression right,
        SourceSpan span)
        => new(left, "+", right, span);

    private static VariableExpression Variable(string name, SourceSpan span)
        => new(name, span);

    private static LiteralExpression Literal(long value, SourceSpan span)
        => new(SandboxValue.FromInt64(value), span);

    private static LiteralExpression LiteralI32(int value, SourceSpan span)
        => new(SandboxValue.FromInt32(value), span);

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

    private sealed record Setup(
        FunctionFrameLayout Layout,
        SandboxFunction Function,
        ForRangeStatement Statement,
        I64ForLoopPlan Plan,
        InterpreterFrame AdmissionFrame);
}

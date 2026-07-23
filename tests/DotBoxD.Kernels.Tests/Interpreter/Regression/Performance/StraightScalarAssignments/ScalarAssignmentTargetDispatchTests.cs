using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.StraightScalarAssignments;

public sealed class ScalarAssignmentTargetDispatchTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Theory]
    [InlineData("I32")]
    [InlineData("I64")]
    [InlineData("F64")]
    public async Task Known_raw_target_is_committed_and_marked_assigned(string type)
    {
        using var host = SandboxTestHost.Create();
        var plan = await PreparePlanAsync(host);
        var value = Scalar(type, 42);
        var assignment = Assign("target", value);
        var function = Function(assignment);
        using var harness = StatementHarness.Create(plan, function);
        var targetSlot = harness.Frame.GetSlot("target");

        Assert.False(harness.Frame.IsSlotAssigned(targetSlot));

        harness.Execute(assignment);

        Assert.True(harness.Frame.IsSlotAssigned(targetSlot));
        Assert.Equal(value, harness.Frame.Read("target"));
        Assert.Equal(2, harness.Meter.FuelUsed);
    }

    [Fact]
    public async Task Unknown_target_is_reported_after_a_successful_rhs_evaluation()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PreparePlanAsync(host);
        using var harness = StatementHarness.Create(plan, Function());
        var assignment = Assign("missing", SandboxValue.FromInt64(42));

        var error = Assert.Throws<SandboxRuntimeException>(() => harness.Execute(assignment));

        Assert.Equal(SandboxErrorCode.ValidationError, error.Error.Code);
        Assert.Equal("unknown local 'missing' at runtime", error.Error.SafeMessage);
        Assert.Equal(2, harness.Meter.FuelUsed);
    }

    [Fact]
    public async Task Unknown_target_does_not_hide_an_earlier_rhs_fault()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PreparePlanAsync(host);
        using var harness = StatementHarness.Create(plan, Function());
        var assignment = new AssignmentStatement(
            "missing",
            new BinaryExpression(
                Literal(SandboxValue.FromInt64(1)),
                "/",
                Literal(SandboxValue.FromInt64(0)),
                Span),
            Span);

        var error = Assert.Throws<SandboxRuntimeException>(() => harness.Execute(assignment));

        Assert.Equal(SandboxErrorCode.InvalidInput, error.Error.Code);
        Assert.Equal("integer division by zero", error.Error.SafeMessage);
        Assert.Equal(4, harness.Meter.FuelUsed);
    }

    [Fact]
    public async Task Matching_evaluator_fault_does_not_mark_the_raw_target_assigned()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PreparePlanAsync(host);
        var layoutAssignment = Assign("target", SandboxValue.FromInt64(0));
        using var harness = StatementHarness.Create(plan, Function(layoutAssignment));
        var targetSlot = harness.Frame.GetSlot("target");
        var failingAssignment = new AssignmentStatement(
            "target",
            new BinaryExpression(
                Literal(SandboxValue.FromInt64(1)),
                "/",
                Literal(SandboxValue.FromInt64(0)),
                Span),
            Span);

        var error = Assert.Throws<SandboxRuntimeException>(() => harness.Execute(failingAssignment));

        Assert.Equal("integer division by zero", error.Error.SafeMessage);
        Assert.False(harness.Frame.IsSlotAssigned(targetSlot));
        Assert.Equal(4, harness.Meter.FuelUsed);
    }

    [Fact]
    public async Task Targeted_miss_runs_the_legacy_cascade_without_committing()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PreparePlanAsync(host);
        var layoutAssignment = Assign("target", SandboxValue.FromInt64(0));
        using var harness = StatementHarness.Create(plan, Function(layoutAssignment));
        var targetSlot = harness.Frame.GetSlot("target");
        var mismatchedAssignment = Assign("target", SandboxValue.FromInt32(42));

        _ = Assert.Throws<InvalidCastException>(() => harness.Execute(mismatchedAssignment));

        Assert.False(harness.Frame.IsSlotAssigned(targetSlot));
        Assert.Equal(2, harness.Meter.FuelUsed);
    }

    [Theory]
    [InlineData("I32", "i32", "3")]
    [InlineData("I64", "i64", "3")]
    [InlineData("F64", "f64", "0.25")]
    public async Task Shared_plan_keeps_concurrent_raw_assignment_state_isolated(
        string type,
        string literalName,
        string increment)
    {
        const int executionCount = 32;
        using var host = SandboxTestHost.Create();
        var plan = await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Recurrences(
                $"target-dispatch-{type.ToLowerInvariant()}-concurrent",
                type,
                literalName,
                increment,
                count: 8,
                useRawStep: true));
        var interpreter = new SandboxInterpreter();
        var options = StraightScalarAssignmentTestSupport.Options();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var executions = Enumerable.Range(0, executionCount)
            .Select(index => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref started) == executionCount)
                {
                    ready.SetResult();
                }

                await start.Task;
                var initial = index + 1D;
                var step = (index % 4) + 1D;
                var input = SandboxValue.FromList(
                    [Scalar(type, initial), Scalar(type, step)],
                    ScalarType(type));
                var result = await interpreter.ExecuteAsync(
                    plan,
                    "main",
                    input,
                    options,
                    CancellationToken.None);
                return (Expected: initial + (8 * step), Result: result);
            }))
            .ToArray();

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        start.SetResult();
        var outcomes = await Task.WhenAll(executions);

        foreach (var outcome in outcomes)
        {
            Assert.True(outcome.Result.Succeeded, outcome.Result.Error?.SafeMessage);
            Assert.Equal(outcome.Expected, NumericValue(outcome.Result.Value!));
            StraightScalarAssignmentTestSupport.AssertUsage(
                outcome.Result.ResourceUsage,
                fuel: 35,
                collectionElements: 2);
        }
    }

    private static async Task<ExecutionPlan> PreparePlanAsync(DotBoxD.Hosting.Execution.SandboxHost host)
        => await StraightScalarAssignmentTestSupport.PrepareAsync(
            host,
            StraightScalarAssignmentModules.Assignment(
                "target-dispatch-harness",
                "I64",
                "I64",
                "value",
                """{ "var": "value" }"""));

    private static SandboxFunction Function(params AssignmentStatement[] assignments)
        => new("dispatch-frame", false, [], SandboxType.I64, assignments);

    private static AssignmentStatement Assign(string name, SandboxValue value)
        => new(name, Literal(value), Span);

    private static LiteralExpression Literal(SandboxValue value) => new(value, Span);

    private static SandboxValue Scalar(string type, double value)
        => type switch
        {
            "I32" => SandboxValue.FromInt32((int)value),
            "I64" => SandboxValue.FromInt64((long)value),
            "F64" => SandboxValue.FromDouble(value),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown scalar type")
        };

    private static SandboxType ScalarType(string type)
        => type switch
        {
            "I32" => SandboxType.I32,
            "I64" => SandboxType.I64,
            "F64" => SandboxType.F64,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown scalar type")
        };

    private static double NumericValue(SandboxValue value)
        => value switch
        {
            I32Value number => number.Value,
            I64Value number => number.Value,
            F64Value number => number.Value,
            _ => throw new Xunit.Sdk.XunitException("unexpected scalar assignment value")
        };

    private sealed class StatementHarness : IDisposable
    {
        private readonly SandboxContext _context;

        private StatementHarness(
            SandboxContext context,
            ResourceMeter meter,
            StatementExecutor executor,
            InterpreterFrame frame)
        {
            _context = context;
            Meter = meter;
            Executor = executor;
            Frame = frame;
        }

        public ResourceMeter Meter { get; }
        public StatementExecutor Executor { get; }
        public InterpreterFrame Frame { get; }

        public static StatementHarness Create(ExecutionPlan plan, SandboxFunction function)
        {
            var meter = new ResourceMeter(plan.Budget);
            var context = new SandboxContext(
                SandboxRunId.New(),
                plan.Policy,
                meter,
                plan.Bindings,
                new InMemoryAuditSink(),
                CancellationToken.None);
            var layout = FunctionFrameLayout.Build(function, plan.FunctionAnalysis, plan.Bindings);
            var frame = InterpreterFrame.Create(layout, function, LocalFunctionArguments.FromArray([]));
            var evaluator = new InterpreterEvaluator(
                plan,
                context,
                StraightScalarAssignmentTestSupport.Options(),
                new FunctionFrameLayoutCache(plan));
            return new StatementHarness(context, meter, new StatementExecutor(evaluator), frame);
        }

        public void Execute(AssignmentStatement assignment)
        {
            var pending = Executor.ExecuteStatementAsync(assignment, Frame);
            Assert.True(pending.IsCompletedSuccessfully, "assignment unexpectedly became asynchronous");
            Assert.Null(pending.Result);
        }

        public void Dispose() => _context.Dispose();
    }
}

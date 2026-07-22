using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class I32WhileLoopPlanCacheTests
{
    [Fact]
    public async Task Repeated_nested_entry_reuses_the_exact_while_plan()
    {
        const int iterations = 20_000;
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I32WhileLoopPlanCacheModules.NestedEntry, Policy());
        var input = SandboxValue.FromInt32(iterations);
        ForceGc();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var result = Execute(new SandboxInterpreter(), plan, input);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(iterations, ((I32Value)result.Value!).Value);
        Assert.Equal(10L + (13L * iterations), result.ResourceUsage.FuelUsed);
        Assert.Equal(iterations, result.ResourceUsage.LoopIterations);
        Assert.True(
            allocated < 10_000,
            $"Nested while entry allocated {allocated:N0} B; rebuilding each plan costs about 5.6 MB.");
    }

    [Fact]
    public void Cached_plan_revalidates_condition_and_body_slots_and_uses_reference_keys()
    {
        var setup = CreateDirectPlan();
        ref var plans = ref setup.Layout.LoopPlans;
        Assert.False(plans.ShouldCacheI32WhilePlan(setup.Statement));
        Assert.True(plans.ShouldCacheI32WhilePlan(setup.Statement));
        plans.CacheI32WhilePlan(setup.Plan);

        Assert.True(plans.TryGetI32WhilePlan(setup.Statement, setup.AssignedFrame, out var cached));
        Assert.Same(setup.Plan, cached);

        var equalStatement = setup.Statement with { };
        Assert.Equal(setup.Statement, equalStatement);
        Assert.NotSame(setup.Statement, equalStatement);
        Assert.False(plans.TryGetI32WhilePlan(equalStatement, setup.AssignedFrame, out _));

        var missingConditionSlot = CreateFrame(setup.Layout, setup.Function, "counter", "divisor");
        Assert.False(plans.TryGetI32WhilePlan(setup.Statement, missingConditionSlot, out _));

        var missingBodySlot = CreateFrame(setup.Layout, setup.Function, "counter", "limit");
        Assert.False(plans.TryGetI32WhilePlan(setup.Statement, missingBodySlot, out _));
    }

    [Theory]
    [InlineData(15L, 10L, SandboxErrorCode.QuotaExceeded, "fuel exhausted", 16L, 1L)]
    [InlineData(16L, 10L, SandboxErrorCode.InvalidInput, "integer division by zero", 16L, 1L)]
    [InlineData(100L, 0L, SandboxErrorCode.QuotaExceeded, "loop iteration budget exhausted", 7L, 1L)]
    public async Task Cached_plan_preserves_fault_and_metering_order(
        long maxFuel,
        long maxLoopIterations,
        SandboxErrorCode expectedCode,
        string expectedMessage,
        long expectedFuel,
        long expectedLoops)
    {
        using var host = SandboxTestHost.Create();
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(maxFuel)
            .WithMaxLoopIterations(maxLoopIterations)
            .Build();
        var plan = await PrepareAsync(host, I32WhileLoopPlanCacheModules.FaultOrdering, policy);
        var cachedInterpreter = new SandboxInterpreter();

        _ = Execute(cachedInterpreter, plan, Input(limit: 0, divisor: 1));
        _ = Execute(cachedInterpreter, plan, Input(limit: 0, divisor: 1));
        var cached = Execute(cachedInterpreter, plan, Input(limit: 1, divisor: 0));
        var fresh = Execute(new SandboxInterpreter(), plan, Input(limit: 1, divisor: 0));

        Assert.Equal(fresh.Error, cached.Error);
        Assert.Equal(fresh.ResourceUsage, cached.ResourceUsage);
        Assert.False(cached.Succeeded);
        Assert.Equal(expectedCode, cached.Error!.Code);
        Assert.Equal(expectedMessage, cached.Error.SafeMessage);
        Assert.Equal(expectedFuel, cached.ResourceUsage.FuelUsed);
        Assert.Equal(expectedLoops, cached.ResourceUsage.LoopIterations);
    }

    [Fact]
    public async Task Debug_trace_uses_the_traced_path_after_cache_warmup()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I32WhileLoopPlanCacheModules.Counter, Policy());
        var interpreter = new SandboxInterpreter();

        _ = Execute(interpreter, plan, SandboxValue.FromInt32(1));
        _ = Execute(interpreter, plan, SandboxValue.FromInt32(1));
        var traced = Execute(interpreter, plan, SandboxValue.FromInt32(1), debug: true);

        Assert.True(traced.Succeeded, traced.Error?.SafeMessage);
        Assert.Contains(traced.AuditEvents, audit => IsTrace(audit, "WhileStatement"));
        Assert.Equal(2, traced.AuditEvents.Count(audit => IsTrace(audit, "AssignmentStatement")));
    }

    [Fact]
    public async Task Concurrent_first_executions_publish_a_valid_plan()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, I32WhileLoopPlanCacheModules.Counter, Policy());
        var interpreter = new SandboxInterpreter();
        using var start = new ManualResetEventSlim();
        var executions = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return Execute(interpreter, plan, SandboxValue.FromInt32(100));
            }))
            .ToArray();

        start.Set();
        var results = await Task.WhenAll(executions);

        Assert.All(results, result =>
        {
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal(100, ((I32Value)result.Value!).Value);
            Assert.Equal(100, result.ResourceUsage.LoopIterations);
        });
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
                if (plans.ShouldCacheI32WhilePlan(setup.Statement))
                {
                    plans.CacheI32WhilePlan(setup.Plan);
                }
            }))
            .ToArray();

        start.Set();
        await Task.WhenAll(admissions);

        ref var publishedPlans = ref setup.Layout.LoopPlans;
        Assert.True(publishedPlans.TryGetI32WhilePlan(
            setup.Statement,
            setup.AssignedFrame,
            out var published));
        Assert.Same(setup.Plan, published);
    }

    private static DirectPlanSetup CreateDirectPlan()
    {
        var span = new SourceSpan(1, 1);
        var condition = new BinaryExpression(Variable("counter", span), "<", Variable("limit", span), span);
        var expression = new BinaryExpression(Literal(1, span), "/", Variable("divisor", span), span);
        var assignment = new AssignmentStatement("counter", expression, span);
        var statement = new WhileStatement(condition, [assignment], span);
        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I32,
            [
                new AssignmentStatement("counter", Literal(0, span), span),
                new AssignmentStatement("limit", Literal(0, span), span),
                new AssignmentStatement("divisor", Literal(1, span), span),
                statement
            ]);
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            new BindingRegistryBuilder().Build());
        var frame = CreateFrame(layout, function, "counter", "limit", "divisor");
        Assert.True(I32ComparisonPlan.TryCreate(condition, frame, "", out var conditionPlan));
        Assert.True(I32ExpressionPlan.TryCreate(expression, frame, "", out var expressionPlan));
        var plan = new I32WhileLoopPlan(
            statement,
            conditionPlan,
            frame.GetSlot("counter"),
            expressionPlan,
            bodyFuel: 1 + expressionPlan.FuelCost);
        return new DirectPlanSetup(layout, function, statement, plan, frame);
    }

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

    private static bool IsTrace(SandboxAuditEvent audit, string node)
        => audit.Message?.Contains($"node=statement:{node}", StringComparison.Ordinal) == true;

    private static SandboxExecutionResult Execute(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        bool debug = false)
    {
        var pending = interpreter.ExecuteAsync(plan, "main", input, Options(debug), CancellationToken.None);
        Assert.True(pending.IsCompletedSuccessfully, "while-plan execution unexpectedly became asynchronous");
        return pending.Result;
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        string moduleJson,
        SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, policy);
    }

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxTotalCollectionElements(long.MaxValue)
            .Build();

    private static SandboxExecutionOptions Options(bool debug = false)
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true,
            EnableDebugTrace = debug
        };

    private static SandboxValue Input(int limit, int divisor)
        => SandboxValue.FromList(
            [SandboxValue.FromInt32(limit), SandboxValue.FromInt32(divisor)],
            SandboxType.I32);

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record DirectPlanSetup(
        FunctionFrameLayout Layout,
        SandboxFunction Function,
        WhileStatement Statement,
        I32WhileLoopPlan Plan,
        InterpreterFrame AssignedFrame);
}

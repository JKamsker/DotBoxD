using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.NestedLoops;

public sealed class NestedI32ForLoopParityTests
{
    [Fact]
    public async Task Cached_composite_path_reevaluates_mutated_inner_bound()
    {
        using var optimizedHost = SandboxTestHost.Create();
        using var controlHost = SandboxTestHost.Create();
        var policy = Policy();
        var optimizedPlan = await PrepareAsync(
            optimizedHost,
            I32NestedLoopPlanCacheModules.MutatingInnerBound,
            policy);
        var controlPlan = await PrepareAsync(
            controlHost,
            I32NestedLoopPlanCacheModules.MutatingInnerBound,
            policy);
        var interpreter = new SandboxInterpreter();

        Assert.True(Execute(interpreter, optimizedPlan, Input(2, 1)).Succeeded);
        Assert.True(Execute(interpreter, optimizedPlan, Input(2, 1)).Succeeded);

        var optimized = Execute(interpreter, optimizedPlan, Input(2, 3));
        var generic = Execute(new SandboxInterpreter(), controlPlan, Input(2, 3));

        AssertEquivalent(generic, optimized);
        Assert.True(optimized.Succeeded, optimized.Error?.SafeMessage);
        Assert.Equal(0, ((I32Value)optimized.Value!).Value);
        Assert.Equal(5, optimized.ResourceUsage.LoopIterations);
    }

    [Fact]
    public async Task Cached_composite_path_preserves_inner_arithmetic_fault_order()
    {
        using var optimizedHost = SandboxTestHost.Create();
        using var controlHost = SandboxTestHost.Create();
        var policy = Policy();
        var optimizedPlan = await PrepareAsync(
            optimizedHost,
            I32NestedLoopPlanCacheModules.FaultingInnerAssignment,
            policy);
        var controlPlan = await PrepareAsync(
            controlHost,
            I32NestedLoopPlanCacheModules.FaultingInnerAssignment,
            policy);
        var interpreter = new SandboxInterpreter();

        Assert.True(Execute(interpreter, optimizedPlan, Input(2, 1)).Succeeded);
        Assert.True(Execute(interpreter, optimizedPlan, Input(2, 1)).Succeeded);

        var optimized = Execute(interpreter, optimizedPlan, Input(2, 0));
        var generic = Execute(new SandboxInterpreter(), controlPlan, Input(2, 0));

        AssertEquivalent(generic, optimized);
        Assert.False(optimized.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, optimized.Error!.Code);
        Assert.Equal("integer division by zero", optimized.Error.SafeMessage);
    }

    [Fact]
    public async Task Cached_composite_path_keeps_outer_end_captured_before_body_mutation()
    {
        using var optimizedHost = SandboxTestHost.Create();
        using var controlHost = SandboxTestHost.Create();
        var policy = Policy();
        var optimizedPlan = await PrepareAsync(
            optimizedHost,
            I32NestedLoopPlanCacheModules.MutatingOuterBound,
            policy);
        var controlPlan = await PrepareAsync(
            controlHost,
            I32NestedLoopPlanCacheModules.MutatingOuterBound,
            policy);
        var interpreter = new SandboxInterpreter();

        Assert.True(Execute(interpreter, optimizedPlan, Input(1, 1)).Succeeded);
        Assert.True(Execute(interpreter, optimizedPlan, Input(1, 1)).Succeeded);

        var optimized = Execute(interpreter, optimizedPlan, Input(3, 1));
        var generic = Execute(new SandboxInterpreter(), controlPlan, Input(3, 1));

        AssertEquivalent(generic, optimized);
        Assert.True(optimized.Succeeded, optimized.Error?.SafeMessage);
        Assert.Equal(0, ((I32Value)optimized.Value!).Value);
        Assert.Equal(6, optimized.ResourceUsage.LoopIterations);
    }

    [Fact]
    public async Task Cached_composite_path_supports_outer_index_bounds_and_expressions()
    {
        using var optimizedHost = SandboxTestHost.Create();
        using var controlHost = SandboxTestHost.Create();
        var policy = Policy();
        var optimizedPlan = await PrepareAsync(
            optimizedHost,
            I32NestedLoopPlanCacheModules.OuterIndexDependent,
            policy);
        var controlPlan = await PrepareAsync(
            controlHost,
            I32NestedLoopPlanCacheModules.OuterIndexDependent,
            policy);
        var interpreter = new SandboxInterpreter();

        Assert.True(Execute(interpreter, optimizedPlan, Input(8, 0)).Succeeded);
        Assert.True(Execute(interpreter, optimizedPlan, Input(8, 0)).Succeeded);

        var optimized = Execute(interpreter, optimizedPlan, Input(8, 0));
        var generic = Execute(new SandboxInterpreter(), controlPlan, Input(8, 0));

        AssertEquivalent(generic, optimized);
        Assert.True(optimized.Succeeded, optimized.Error?.SafeMessage);
        Assert.Equal(140, ((I32Value)optimized.Value!).Value);
        Assert.Equal(380, optimized.ResourceUsage.FuelUsed);
        Assert.Equal(36, optimized.ResourceUsage.LoopIterations);
    }

    [Theory]
    [InlineData(32L, long.MaxValue, "fuel exhausted")]
    [InlineData(1_000L, 2L, "loop iteration budget exhausted")]
    public async Task Cached_composite_path_preserves_quota_fault_order(
        long maxFuel,
        long maxLoopIterations,
        string expectedMessage)
    {
        using var optimizedHost = SandboxTestHost.Create();
        using var controlHost = SandboxTestHost.Create();
        var policy = Policy(maxFuel, maxLoopIterations);
        var optimizedPlan = await PrepareAsync(
            optimizedHost,
            I32NestedLoopPlanCacheModules.NestedLoop,
            policy);
        var controlPlan = await PrepareAsync(
            controlHost,
            I32NestedLoopPlanCacheModules.NestedLoop,
            policy);
        var interpreter = new SandboxInterpreter();

        Assert.True(Execute(interpreter, optimizedPlan, Input(1, 1)).Succeeded);
        Assert.True(Execute(interpreter, optimizedPlan, Input(1, 1)).Succeeded);

        var optimized = Execute(interpreter, optimizedPlan, Input(2, 1));
        var generic = Execute(new SandboxInterpreter(), controlPlan, Input(2, 1));

        AssertEquivalent(generic, optimized);
        Assert.False(optimized.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, optimized.Error!.Code);
        Assert.Equal(expectedMessage, optimized.Error.SafeMessage);
    }

    private static void AssertEquivalent(
        SandboxExecutionResult expected,
        SandboxExecutionResult actual)
    {
        Assert.Equal(expected.Succeeded, actual.Succeeded);
        Assert.Equal(expected.Error, actual.Error);
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.ResourceUsage, actual.ResourceUsage);
    }

    private static SandboxExecutionResult Execute(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input)
    {
        var pending = interpreter.ExecuteAsync(
            plan,
            "main",
            input,
            Options(),
            CancellationToken.None);
        Assert.True(pending.IsCompletedSuccessfully, "nested-loop execution unexpectedly became asynchronous");
        return pending.Result;
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        string moduleJson,
        SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, policy);
    }

    private static SandboxValue Input(int first, int second)
        => SandboxValue.FromList(
            [SandboxValue.FromInt32(first), SandboxValue.FromInt32(second)],
            SandboxType.I32);

    private static SandboxPolicy Policy(
        long maxFuel = long.MaxValue,
        long maxLoopIterations = long.MaxValue)
        => SandboxPolicyBuilder.Create()
            .WithFuel(maxFuel)
            .WithMaxLoopIterations(maxLoopIterations)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxTotalCollectionElements(long.MaxValue)
            .Build();

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };
}

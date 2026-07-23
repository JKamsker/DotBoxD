using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.MultiAssignment;

public sealed class MultiAssignmentLoopSemanticsTests
{
    [Fact]
    public async Task Cached_for_range_plan_uses_current_values_in_source_order()
    {
        using var cachedHost = SandboxTestHost.Create();
        using var freshHost = SandboxTestHost.Create();
        var cachedPlan = await MultiAssignmentLoopTestRuntime.PrepareAsync(
            cachedHost,
            MultiAssignmentRuntimeModules.OrderedForRange);
        var freshPlan = await MultiAssignmentLoopTestRuntime.PrepareAsync(
            freshHost,
            MultiAssignmentRuntimeModules.OrderedForRange);
        var interpreter = new SandboxInterpreter();

        Assert.True(Execute(interpreter, cachedPlan, 1, 1).Succeeded);
        Assert.True(Execute(interpreter, cachedPlan, 1, 1).Succeeded);
        var cached = Execute(interpreter, cachedPlan, 3, 4);
        var fresh = Execute(new SandboxInterpreter(), freshPlan, 3, 4);

        MultiAssignmentLoopTestRuntime.AssertEquivalent(fresh, cached);
        Assert.True(cached.Succeeded, cached.Error?.SafeMessage);
        Assert.Equal(14, ((I32Value)cached.Value!).Value);
        Assert.Equal(3, cached.ResourceUsage.LoopIterations);
    }

    [Fact]
    public async Task Cached_three_assignment_plan_preserves_the_complete_dependency_chain()
    {
        using var cachedHost = SandboxTestHost.Create();
        using var freshHost = SandboxTestHost.Create();
        var cachedPlan = await MultiAssignmentLoopTestRuntime.PrepareAsync(
            cachedHost,
            MultiAssignmentRuntimeModules.OrderedThreeForRange);
        var freshPlan = await MultiAssignmentLoopTestRuntime.PrepareAsync(
            freshHost,
            MultiAssignmentRuntimeModules.OrderedThreeForRange);
        var interpreter = new SandboxInterpreter();

        var warmupInput = MultiAssignmentLoopTestRuntime.Input(1, 1, 1);
        Assert.True(MultiAssignmentLoopTestRuntime.Execute(interpreter, cachedPlan, warmupInput).Succeeded);
        Assert.True(MultiAssignmentLoopTestRuntime.Execute(interpreter, cachedPlan, warmupInput).Succeeded);
        var currentInput = MultiAssignmentLoopTestRuntime.Input(3, 4, 7);
        var cached = MultiAssignmentLoopTestRuntime.Execute(interpreter, cachedPlan, currentInput);
        var fresh = MultiAssignmentLoopTestRuntime.Execute(
            new SandboxInterpreter(),
            freshPlan,
            currentInput);

        MultiAssignmentLoopTestRuntime.AssertEquivalent(fresh, cached);
        Assert.True(cached.Succeeded, cached.Error?.SafeMessage);
        Assert.Equal(21, ((I32Value)cached.Value!).Value);
        Assert.Equal(63, cached.ResourceUsage.FuelUsed);
        Assert.Equal(3, cached.ResourceUsage.LoopIterations);
        Assert.Equal(0, cached.ResourceUsage.AllocatedBytes);
        Assert.Equal(0, cached.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Cached_while_plan_uses_current_values_in_source_order()
    {
        using var cachedHost = SandboxTestHost.Create();
        using var freshHost = SandboxTestHost.Create();
        var cachedPlan = await MultiAssignmentLoopTestRuntime.PrepareAsync(
            cachedHost,
            MultiAssignmentRuntimeModules.OrderedWhile);
        var freshPlan = await MultiAssignmentLoopTestRuntime.PrepareAsync(
            freshHost,
            MultiAssignmentRuntimeModules.OrderedWhile);
        var interpreter = new SandboxInterpreter();

        Assert.True(Execute(interpreter, cachedPlan, 1, 1).Succeeded);
        Assert.True(Execute(interpreter, cachedPlan, 1, 1).Succeeded);
        var cached = Execute(interpreter, cachedPlan, 5, 2);
        var fresh = Execute(new SandboxInterpreter(), freshPlan, 5, 2);

        MultiAssignmentLoopTestRuntime.AssertEquivalent(fresh, cached);
        Assert.True(cached.Succeeded, cached.Error?.SafeMessage);
        Assert.Equal(8, ((I32Value)cached.Value!).Value);
        Assert.Equal(3, cached.ResourceUsage.LoopIterations);
    }

    [Theory]
    [InlineData(long.MaxValue, long.MaxValue, 0, SandboxErrorCode.InvalidInput, "integer division by zero")]
    [InlineData(19L, long.MaxValue, 1, SandboxErrorCode.QuotaExceeded, "fuel exhausted")]
    [InlineData(1_000L, 0L, 1, SandboxErrorCode.QuotaExceeded, "loop iteration budget exhausted")]
    public async Task Cached_for_range_plan_matches_fresh_fault_and_quota_order(
        long maxFuel,
        long maxLoopIterations,
        int divisor,
        SandboxErrorCode expectedCode,
        string expectedMessage)
    {
        await AssertFailureParityAsync(
            MultiAssignmentRuntimeModules.FaultingForRange,
            MultiAssignmentLoopTestRuntime.Policy(maxFuel, maxLoopIterations),
            MultiAssignmentLoopTestRuntime.Input(1, divisor),
            expectedCode,
            expectedMessage);
    }

    [Theory]
    [InlineData(long.MaxValue, long.MaxValue, 0, SandboxErrorCode.InvalidInput, "integer division by zero")]
    [InlineData(20L, long.MaxValue, 1, SandboxErrorCode.QuotaExceeded, "fuel exhausted")]
    [InlineData(1_000L, 0L, 1, SandboxErrorCode.QuotaExceeded, "loop iteration budget exhausted")]
    public async Task Cached_while_plan_matches_fresh_fault_and_quota_order(
        long maxFuel,
        long maxLoopIterations,
        int divisor,
        SandboxErrorCode expectedCode,
        string expectedMessage)
    {
        await AssertFailureParityAsync(
            MultiAssignmentRuntimeModules.FaultingWhile,
            MultiAssignmentLoopTestRuntime.Policy(maxFuel, maxLoopIterations),
            MultiAssignmentLoopTestRuntime.Input(1, divisor),
            expectedCode,
            expectedMessage);
    }

    private static async Task AssertFailureParityAsync(
        string moduleJson,
        SandboxPolicy policy,
        SandboxValue input,
        SandboxErrorCode expectedCode,
        string expectedMessage)
    {
        using var cachedHost = SandboxTestHost.Create();
        using var freshHost = SandboxTestHost.Create();
        var cachedPlan = await MultiAssignmentLoopTestRuntime.PrepareAsync(
            cachedHost,
            moduleJson,
            policy);
        var freshPlan = await MultiAssignmentLoopTestRuntime.PrepareAsync(
            freshHost,
            moduleJson,
            policy);
        var interpreter = new SandboxInterpreter();

        Assert.False(MultiAssignmentLoopTestRuntime.Execute(interpreter, cachedPlan, input).Succeeded);
        Assert.False(MultiAssignmentLoopTestRuntime.Execute(interpreter, cachedPlan, input).Succeeded);
        var cached = MultiAssignmentLoopTestRuntime.Execute(interpreter, cachedPlan, input);
        var fresh = MultiAssignmentLoopTestRuntime.Execute(
            new SandboxInterpreter(),
            freshPlan,
            input);

        MultiAssignmentLoopTestRuntime.AssertEquivalent(fresh, cached);
        Assert.False(cached.Succeeded);
        Assert.Equal(expectedCode, cached.Error!.Code);
        Assert.Equal(expectedMessage, cached.Error.SafeMessage);
    }

    private static SandboxExecutionResult Execute(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        int first,
        int second)
        => MultiAssignmentLoopTestRuntime.Execute(
            interpreter,
            plan,
            MultiAssignmentLoopTestRuntime.Input(first, second));
}

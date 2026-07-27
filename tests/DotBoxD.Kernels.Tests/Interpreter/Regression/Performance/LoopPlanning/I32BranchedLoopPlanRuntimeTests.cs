using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class I32BranchedLoopPlanRuntimeTests
{
    [Fact]
    public async Task Cached_plan_reads_current_frame_values()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, BranchedLoopAllocationModules.InputDependent(), Policy());
        var cachedInterpreter = new SandboxInterpreter();

        Assert.True(Execute(cachedInterpreter, plan, 1).Succeeded);
        Assert.True(Execute(cachedInterpreter, plan, 1).Succeeded);
        var cached = Execute(cachedInterpreter, plan, 3);
        var fresh = Execute(new SandboxInterpreter(), plan, 3);

        Assert.True(cached.Succeeded, cached.Error?.SafeMessage);
        Assert.Equal(fresh.Value, cached.Value);
        Assert.Equal(fresh.ResourceUsage, cached.ResourceUsage);
        Assert.Equal(10, ((I32Value)cached.Value!).Value);
    }

    [Fact]
    public async Task Cached_plan_preserves_arithmetic_fault_order()
    {
        using var host = SandboxTestHost.Create();
        var plan = await PrepareAsync(host, BranchedLoopAllocationModules.FaultingAssignment(), Policy());
        var cachedInterpreter = new SandboxInterpreter();

        Assert.True(Execute(cachedInterpreter, plan, 2).Succeeded);
        Assert.True(Execute(cachedInterpreter, plan, 2).Succeeded);
        var cached = Execute(cachedInterpreter, plan, 1);
        var fresh = Execute(new SandboxInterpreter(), plan, 1);

        Assert.Equal(fresh.Error, cached.Error);
        Assert.Equal(fresh.ResourceUsage, cached.ResourceUsage);
        Assert.False(cached.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, cached.Error!.Code);
        Assert.Equal("integer division by zero", cached.Error.SafeMessage);
    }

    [Theory]
    [InlineData(20L, 10L)]
    [InlineData(100L, 0L)]
    public async Task Cached_plan_preserves_quota_fault_order(long maxFuel, long maxLoopIterations)
    {
        using var host = SandboxTestHost.Create();
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(maxFuel)
            .WithMaxLoopIterations(maxLoopIterations)
            .Build();
        var plan = await PrepareAsync(host, BranchedLoopAllocationModules.OneAssignment("i32"), policy);
        var cachedInterpreter = new SandboxInterpreter();

        _ = Execute(cachedInterpreter, plan, 1);
        _ = Execute(cachedInterpreter, plan, 1);
        var cached = Execute(cachedInterpreter, plan, 1);
        var fresh = Execute(new SandboxInterpreter(), plan, 1);

        Assert.Equal(fresh.Error, cached.Error);
        Assert.Equal(fresh.ResourceUsage, cached.ResourceUsage);
        Assert.False(cached.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, cached.Error!.Code);
    }

    private static SandboxExecutionResult Execute(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        int iterations)
    {
        var pending = interpreter.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            Options(),
            CancellationToken.None);
        Assert.True(pending.IsCompletedSuccessfully, "branched-plan execution unexpectedly became asynchronous");
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
            .WithFuel(1_000)
            .WithMaxLoopIterations(10)
            .Build();

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };
}

using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

public sealed class BranchedF64BulkMeteringTests
{
    [Fact]
    public async Task Equal_cost_branches_preserve_value_and_resource_usage()
    {
        var result = await ExecuteAsync(
            BranchedLoopAllocationModules.OneAssignment("f64"),
            Policy(maxFuel: 1_000, maxLoopIterations: 10),
            iterations: 3);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(9.0, ((F64Value)result.Value!).Value);
        AssertUsage(result, fuel: 53, loopIterations: 3);
    }

    [Fact]
    public async Task Unequal_cost_branches_keep_path_specific_metering()
    {
        var result = await ExecuteAsync(
            BranchedLoopAllocationModules.UnequalBranchFuel("f64"),
            Policy(maxFuel: 1_000, maxLoopIterations: 10),
            iterations: 3);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(6.0, ((F64Value)result.Value!).Value);
        AssertUsage(result, fuel: 51, loopIterations: 3);
    }

    [Theory]
    [InlineData(30L, 10L, 32L, 2L, "fuel exhausted")]
    [InlineData(1_000L, 2L, 36L, 3L, "loop iteration budget exhausted")]
    public async Task Insufficient_bulk_budget_retains_incremental_quota_order(
        long maxFuel,
        long maxLoopIterations,
        long expectedFuel,
        long expectedLoopIterations,
        string expectedMessage)
    {
        var result = await ExecuteAsync(
            BranchedLoopAllocationModules.OneAssignment("f64"),
            Policy(maxFuel, maxLoopIterations),
            iterations: 3);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(expectedMessage, result.Error.SafeMessage);
        AssertUsage(result, expectedFuel, expectedLoopIterations);
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        string moduleJson,
        SandboxPolicy policy,
        int iterations)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Interpreted,
                AllowFallbackToInterpreter = false,
                SuppressSuccessfulRunSummaryAudit = true
            });
    }

    private static SandboxPolicy Policy(long maxFuel, long maxLoopIterations)
        => SandboxPolicyBuilder.Create()
            .WithFuel(maxFuel)
            .WithMaxLoopIterations(maxLoopIterations)
            .Build();

    private static void AssertUsage(
        SandboxExecutionResult result,
        long fuel,
        long loopIterations)
    {
        Assert.Equal(fuel, result.ResourceUsage.FuelUsed);
        Assert.Equal(loopIterations, result.ResourceUsage.LoopIterations);
        Assert.Equal(0, result.ResourceUsage.AllocatedBytes);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
    }
}

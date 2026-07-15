using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.StraightScalarAssignments;

internal static class StraightScalarAssignmentTestSupport
{
    public static async Task<ExecutionPlan> PrepareAsync(SandboxHost host, string moduleJson)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, Policy());
    }

    public static async Task<SandboxExecutionResult> ExecuteAsync(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input,
        bool enableDebugTrace = false)
        => await host.ExecuteAsync(plan, "main", input, Options(enableDebugTrace));

    public static SandboxExecutionOptions Options(bool enableDebugTrace = false)
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true,
            EnableDebugTrace = enableDebugTrace
        };

    public static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create().WithFuel(1_000).Build();

    public static ExecutionPlan ReplaceModule(ExecutionPlan plan, SandboxModule module)
        => new(
            plan.ModuleHash,
            plan.PlanHash,
            plan.PlanSeal,
            plan.PolicyHash,
            plan.BindingManifestHash,
            module,
            plan.Policy,
            plan.Bindings,
            plan.Budget,
            plan.FunctionAnalysis,
            plan.BindingReferences);

    public static void AssertUsage(
        SandboxResourceUsage usage,
        long fuel,
        int hostCalls = 0,
        long collectionElements = 0)
    {
        Assert.Equal(fuel, usage.FuelUsed);
        Assert.Equal(1_000, usage.MaxFuel);
        Assert.Equal(0, usage.LoopIterations);
        Assert.Equal(0, usage.AllocatedBytes);
        Assert.Equal(hostCalls, usage.HostCalls);
        Assert.Equal(0, usage.FileBytesRead);
        Assert.Equal(0, usage.FileBytesWritten);
        Assert.Equal(0, usage.NetworkBytesRead);
        Assert.Equal(0, usage.NetworkBytesWritten);
        Assert.Equal(0, usage.LogEvents);
        Assert.Equal(collectionElements, usage.CollectionElements);
        Assert.Equal(0, usage.StringBytes);
    }
}

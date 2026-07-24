using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.GenericExpressions;

internal static class GenericPrimitiveExpressionTestRuntime
{
    public static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        SandboxModule module)
        => await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .AllowPureComputation()
                .WithFuel(1_000)
                .WithMaxAllocatedBytes(long.MaxValue)
                .Build());

    public static SandboxExecutionResult Execute(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxValue? input = null,
        string entrypoint = "main")
    {
        var pending = interpreter.ExecuteAsync(
            plan,
            entrypoint,
            input ?? SandboxValue.Unit,
            options,
            CancellationToken.None);
        return pending.IsCompletedSuccessfully
            ? pending.Result
            : throw new Xunit.Sdk.XunitException(
                "generic expression unexpectedly became asynchronous");
    }

    public static void AssertUsage(
        SandboxResourceUsage usage,
        long fuel,
        int hostCalls = 0)
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
        Assert.Equal(0, usage.CollectionElements);
        Assert.Equal(0, usage.StringBytes);
    }

    public static SandboxExecutionOptions Options(bool enableDebugTrace = false)
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true,
            EnableDebugTrace = enableDebugTrace
        };

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
}

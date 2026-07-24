using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LocalCalls.InlineI32;

internal static class InlineI32LocalFunctionTestSupport
{
    public static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        string moduleJson,
        long maxFuel = 1_000,
        int maxCallDepth = 64)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(maxFuel)
            .WithMaxCallDepth(maxCallDepth)
            .Build();
        return await host.PrepareAsync(module, policy);
    }

    public static async Task<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        int input,
        bool debug = false,
        CancellationToken cancellationToken = default)
        => await new SandboxInterpreter().ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(input),
            Options(debug),
            cancellationToken);

    public static SandboxExecutionOptions Options(bool debug = false)
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true,
            EnableDebugTrace = debug
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

    public static int ReadInt32(SandboxExecutionResult result)
        => Assert.IsType<I32Value>(result.Value).Value;

    public static void AssertUsage(SandboxResourceUsage usage, long fuel)
    {
        Assert.Equal(fuel, usage.FuelUsed);
        Assert.Equal(0, usage.LoopIterations);
        Assert.Equal(0, usage.AllocatedBytes);
        Assert.Equal(0, usage.HostCalls);
        Assert.Equal(0, usage.CollectionElements);
        Assert.Equal(0, usage.StringBytes);
    }

    public static string TraceNode(SandboxAuditEvent audit)
        => $"{audit.Fields!["category"]}:{audit.Fields["nodeKind"]}";
}

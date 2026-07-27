using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.ScalarReturns;

internal static class ScalarReturnTestSupport
{
    public static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        string moduleJson,
        bool allowRuntimeAsync = false)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        var policy = SandboxPolicyBuilder.Create().WithFuel(1_000);
        if (allowRuntimeAsync)
        {
            policy.AllowRuntimeAsync();
        }

        return await host.PrepareAsync(module, policy.Build());
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

    public static SandboxValue Scalar(string type, double value)
        => type == "I64"
            ? SandboxValue.FromInt64((long)value)
            : SandboxValue.FromDouble(value);

    public static double NumericValue(SandboxValue? value)
        => value switch
        {
            I64Value number => number.Value,
            F64Value number => number.Value,
            _ => throw new Xunit.Sdk.XunitException("unexpected scalar-return value")
        };

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

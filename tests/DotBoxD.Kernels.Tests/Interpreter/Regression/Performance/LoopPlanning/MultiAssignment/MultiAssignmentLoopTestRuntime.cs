using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.MultiAssignment;

internal static class MultiAssignmentLoopTestRuntime
{
    public static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        string moduleJson,
        SandboxPolicy? policy = null)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, policy ?? UnlimitedPolicy());
    }

    public static SandboxPolicy UnlimitedPolicy()
        => Policy(long.MaxValue, long.MaxValue);

    public static SandboxPolicy Policy(long maxFuel, long maxLoopIterations)
        => SandboxPolicyBuilder.Create()
            .WithFuel(maxFuel)
            .WithMaxLoopIterations(maxLoopIterations)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxTotalCollectionElements(long.MaxValue)
            .Build();

    public static SandboxExecutionOptions Options(bool debug = false)
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true,
            EnableDebugTrace = debug
        };

    public static SandboxValue Input(params int[] values)
        => values.Length == 1
            ? SandboxValue.FromInt32(values[0])
            : SandboxValue.FromList(
                values.Select(SandboxValue.FromInt32).ToArray(),
                SandboxType.I32);

    public static SandboxExecutionResult Execute(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions? options = null)
    {
        var pending = interpreter.ExecuteAsync(
            plan,
            "main",
            input,
            options ?? Options(),
            CancellationToken.None);
        Assert.True(
            pending.IsCompletedSuccessfully,
            "multi-assignment loop execution unexpectedly became asynchronous");
        return pending.Result;
    }

    public static Measurement MeasureSuccessful(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        int expectedValue,
        int executionCount)
    {
        long checksum = 0;
        ResourceUsageInvariant? expectedUsage = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < executionCount; i++)
        {
            var result = Execute(interpreter, plan, input, options);
            if (!result.Succeeded)
            {
                throw new Xunit.Sdk.XunitException(result.Error?.SafeMessage ?? "execution failed");
            }

            var actual = ((I32Value)result.Value!).Value;
            if (actual != expectedValue)
            {
                throw new Xunit.Sdk.XunitException($"expected {expectedValue}, got {actual}");
            }

            checksum += actual;
            var usage = ResourceUsageInvariant.From(result.ResourceUsage);
            expectedUsage ??= usage;
            if (usage != expectedUsage.Value)
            {
                throw new Xunit.Sdk.XunitException(
                    "resource usage changed between identical executions");
            }
        }

        return new Measurement(
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            expectedUsage ?? default);
    }

    public static void AssertEquivalent(
        SandboxExecutionResult expected,
        SandboxExecutionResult actual)
    {
        Assert.Equal(expected.Succeeded, actual.Succeeded);
        Assert.Equal(expected.Error, actual.Error);
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.ResourceUsage, actual.ResourceUsage);
    }

    public static bool IsTrace(SandboxAuditEvent audit, string node)
        => audit.Kind == "DebugTrace" &&
           audit.Message?.Contains($"node=statement:{node}", StringComparison.Ordinal) == true;

    public static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    internal readonly record struct ResourceUsageInvariant(
        long FuelUsed,
        long LoopIterations,
        long AllocatedBytes,
        int HostCalls)
    {
        public static ResourceUsageInvariant From(SandboxResourceUsage usage)
            => new(usage.FuelUsed, usage.LoopIterations, usage.AllocatedBytes, usage.HostCalls);
    }

    internal readonly record struct Measurement(
        long AllocatedBytes,
        long Checksum,
        ResourceUsageInvariant Usage);
}

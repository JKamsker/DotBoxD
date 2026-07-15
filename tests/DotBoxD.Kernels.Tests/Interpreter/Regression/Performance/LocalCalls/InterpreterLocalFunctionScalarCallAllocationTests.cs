using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LocalCalls;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class InterpreterLocalFunctionScalarCallAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public async Task Arity_one_through_three_omit_caller_arrays()
    {
        using var host = SandboxTestHost.Create();
        var interpreter = new SandboxInterpreter();
        var options = Options();
        var zero = MeasurePair(
            interpreter,
            await PrepareAsync(host, arity: 0),
            SandboxValue.Unit,
            options);
        var one = MeasurePair(
            interpreter,
            await PrepareAsync(host, arity: 1),
            SandboxValue.FromString("a"),
            options);
        var two = MeasurePair(
            interpreter,
            await PrepareAsync(host, arity: 2),
            StringList("a", "b"),
            options);
        var three = MeasurePair(
            interpreter,
            await PrepareAsync(host, arity: 3),
            StringList("a", "b", "c"),
            options);

        var fixedCallAndFrameBytes = zero.DeltaBytes;
        Assert.True(fixedCallAndFrameBytes > 0);
        AssertBytesPerExecution(
            one.DeltaBytes - fixedCallAndFrameBytes,
            expected: 0,
            "arity one (the legacy caller array adds 32 B/execution)");
        AssertBytesPerExecution(
            two.DeltaBytes - fixedCallAndFrameBytes,
            expected: 0,
            "arity two (the legacy caller array adds 40 B/execution)");
        AssertBytesPerExecution(
            three.DeltaBytes - fixedCallAndFrameBytes,
            expected: 0,
            "arity three (the legacy caller array adds 48 B/execution)");
    }

    private static PairMeasurement MeasurePair(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options)
        => new(
            MeasureCase(interpreter, plan, "direct", input, options),
            MeasureCase(interpreter, plan, "call", input, options));

    private static Measurement MeasureCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options)
    {
        _ = Measure(interpreter, plan, entrypoint, input, options, WarmupIterations);
        ForceGc();
        return Measure(interpreter, plan, entrypoint, input, options, MeasurementIterations);
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        int iterations)
    {
        long checksum = 0;
        Usage? expectedUsage = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, entrypoint, input, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("local-call allocation test unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded || result.Value is not I32Value value || value.Value != 7)
            {
                throw new Xunit.Sdk.XunitException(result.Error?.SafeMessage ?? "unexpected execution result");
            }

            checksum += value.Value;
            var usage = Usage.From(result.ResourceUsage);
            expectedUsage ??= usage;
            if (usage != expectedUsage.Value)
            {
                throw new Xunit.Sdk.XunitException("resource usage changed between identical executions");
            }
        }

        return new Measurement(
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            expectedUsage ?? default);
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        int arity)
    {
        var module = await host.ImportJsonAsync(LocalFunctionScalarCallModules.Allocation(arity));
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(100).Build());
    }

    private static SandboxValue StringList(params string[] values)
        => SandboxValue.FromList(values.Select(SandboxValue.FromString).ToArray(), SandboxType.String);

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

    private static void AssertBytesPerExecution(long totalBytes, double expected, string scenario)
    {
        var actual = totalBytes / (double)MeasurementIterations;
        Assert.True(
            Math.Abs(actual - expected) <= 1,
            $"{scenario} expected {expected:F1} B/execution, got {actual:F3} B/execution");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Usage(
        long FuelUsed,
        long LoopIterations,
        long AllocatedBytes,
        int HostCalls)
    {
        public static Usage From(SandboxResourceUsage usage)
            => new(usage.FuelUsed, usage.LoopIterations, usage.AllocatedBytes, usage.HostCalls);
    }

    private readonly record struct Measurement(long AllocatedBytes, long Checksum, Usage Usage);

    private readonly record struct PairMeasurement(Measurement Direct, Measurement Call)
    {
        public long DeltaBytes => Call.AllocatedBytes - Direct.AllocatedBytes;
    }
}

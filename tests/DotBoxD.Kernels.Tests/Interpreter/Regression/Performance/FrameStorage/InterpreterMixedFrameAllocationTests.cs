using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class InterpreterMixedFrameAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public async Task Mixed_frame_omits_assignment_state_for_raw_parameters_only()
    {
        using var host = SandboxTestHost.Create();
        var boxedPlan = await PrepareAsync(host, MixedFrameAssignmentModules.BoxedParameterAndBoxedLocal);
        var i32Plan = await PrepareAsync(
            host,
            MixedFrameAssignmentModules.RawParameterAndBoxedLocal("mixed-frame-raw-i32-parameter", "I32"));
        var i64Plan = await PrepareAsync(
            host,
            MixedFrameAssignmentModules.RawParameterAndBoxedLocal("mixed-frame-raw-i64-parameter", "I64"));
        var f64Plan = await PrepareAsync(
            host,
            MixedFrameAssignmentModules.RawParameterAndBoxedLocal("mixed-frame-raw-f64-parameter", "F64"));
        var rawLocalPlan = await PrepareAsync(host, MixedFrameAssignmentModules.RawParameterBoxedLocalAndRawLocal);
        var interpreter = new SandboxInterpreter();
        var options = Options();

        var boxed = MeasureCase(interpreter, boxedPlan, SandboxValue.FromBool(true), options);
        var i32 = MeasureCase(interpreter, i32Plan, SandboxValue.FromInt32(3), options);
        var i64 = MeasureCase(interpreter, i64Plan, SandboxValue.FromInt64(3), options);
        var f64 = MeasureCase(interpreter, f64Plan, SandboxValue.FromDouble(3), options);
        var rawLocal = MeasureCase(interpreter, rawLocalPlan, SandboxValue.FromInt32(3), options);

        Assert.Equal(boxed.Checksum, i32.Checksum);
        Assert.Equal(boxed.Checksum, i64.Checksum);
        Assert.Equal(boxed.Checksum, f64.Checksum);
        Assert.Equal(boxed.Checksum, rawLocal.Checksum);

        Assert.InRange(PerExecution(i32, boxed), 31.9, 32.1);
        Assert.InRange(PerExecution(i64, boxed), 39.9, 40.1);
        Assert.InRange(PerExecution(f64, boxed), 39.9, 40.1);

        var genuineRawLocalBytes = PerExecution(rawLocal, i32);
        Assert.InRange(genuineRawLocalBytes, 47.9, 48.1);
    }

    private static Measurement MeasureCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options)
    {
        _ = Measure(interpreter, plan, input, options, WarmupIterations);
        ForceGc();
        return Measure(interpreter, plan, input, options, MeasurementIterations);
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        int iterations)
    {
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("mixed-frame allocation test unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded || result.Value is not I32Value value || value.Value != 7)
            {
                throw new Xunit.Sdk.XunitException(result.Error?.SafeMessage ?? "unexpected execution result");
            }

            checksum += value.Value;
        }

        return new Measurement(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        DotBoxD.Hosting.Execution.SandboxHost host,
        string moduleJson)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(100).Build());
    }

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

    private static double PerExecution(Measurement value, Measurement baseline)
        => (value.AllocatedBytes - baseline.AllocatedBytes) / (double)MeasurementIterations;

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(long AllocatedBytes, long Checksum);
}

using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class InterpreterParameterOnlyFrameAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public async Task Parameter_only_raw_frames_omit_assignment_state_storage()
    {
        using var host = SandboxTestHost.Create();
        var zeroPlan = await PrepareAsync(host, ParameterOnlyFrameModules.ZeroParameters);
        var onePlan = await PrepareAsync(host, ParameterOnlyFrameModules.OneRawParameter);
        var twoPlan = await PrepareAsync(host, ParameterOnlyFrameModules.TwoRawParameters);
        var localPlan = await PrepareAsync(host, ParameterOnlyFrameModules.GenuineRawLocal);
        var interpreter = new SandboxInterpreter();
        var options = Options();

        var zero = MeasureCase(interpreter, zeroPlan, SandboxValue.Unit, options);
        var one = MeasureCase(interpreter, onePlan, SandboxValue.FromInt32(7), options);
        var two = MeasureCase(interpreter, twoPlan, Int32List(7, 11), options);
        var local = MeasureCase(interpreter, localPlan, SandboxValue.Unit, options);

        Assert.Equal(zero.Checksum, one.Checksum);
        Assert.Equal(zero.Checksum, two.Checksum);
        Assert.Equal(zero.Checksum, local.Checksum);

        var oneParameterBytes = PerExecution(one, zero);
        var twoParameterBytes = PerExecution(two, zero);
        var genuineLocalBytes = PerExecution(local, zero);
        Assert.True(
            oneParameterBytes < 48,
            $"One raw parameter added {oneParameterBytes:F1} B/execution; " +
            "a parameter-only frame must allocate only its numeric slot array.");
        Assert.True(
            twoParameterBytes < 48,
            $"Two raw parameters added {twoParameterBytes:F1} B/execution; " +
            "a parameter-only frame must allocate only its numeric slot array.");
        Assert.True(
            genuineLocalBytes >= 48,
            $"A genuine raw local added only {genuineLocalBytes:F1} B/execution; " +
            "it must retain assignment-state storage for read-before-assignment checks.");
        Assert.True(
            genuineLocalBytes - oneParameterBytes >= 16,
            $"A genuine raw local was only {genuineLocalBytes - oneParameterBytes:F1} B/execution " +
            "more expensive than a parameter-only frame.");
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
                throw new InvalidOperationException("frame allocation test unexpectedly became asynchronous");
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

    private static SandboxValue Int32List(params int[] values)
        => SandboxValue.FromList(values.Select(SandboxValue.FromInt32).ToArray(), SandboxType.I32);

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

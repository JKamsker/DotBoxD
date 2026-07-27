using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class F64ForLoopPlanAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public async Task Warm_entered_loop_has_no_recurring_plan_allocation()
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(F64ForLoopPlanCacheModules.Counter);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(long.MaxValue)
                .WithMaxLoopIterations(long.MaxValue)
                .WithMaxAllocatedBytes(long.MaxValue)
                .Build());
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        _ = Measure(interpreter, plan, options, loopIterations: 1, WarmupIterations);
        _ = Measure(interpreter, plan, options, loopIterations: 0, WarmupIterations);
        ForceGc();
        var entered = Measure(interpreter, plan, options, loopIterations: 1, MeasurementIterations);
        var empty = Measure(interpreter, plan, options, loopIterations: 0, MeasurementIterations);

        var incrementalBytes = (entered - empty) / (double)MeasurementIterations;
        Assert.InRange(incrementalBytes, -8, 8);
    }

    [Fact]
    public async Task Warm_nested_loop_retains_only_bounded_execution_state()
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(F64ForLoopPlanCacheModules.Nested);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(long.MaxValue)
                .WithMaxLoopIterations(long.MaxValue)
                .WithMaxAllocatedBytes(long.MaxValue)
                .WithMaxTotalCollectionElements(long.MaxValue)
                .Build());
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };
        var input = SandboxValue.FromList(
            [SandboxValue.FromInt32(20_000), SandboxValue.FromInt32(1)],
            SandboxType.I32);

        _ = MeasureNested(interpreter, plan, options, input);
        _ = MeasureNested(interpreter, plan, options, input);
        ForceGc();
        var allocated = MeasureNested(interpreter, plan, options, input);

        Assert.InRange(allocated, 0, 10_000);
    }

    private static long Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        int loopIterations,
        int iterations)
    {
        var input = SandboxValue.FromInt32(loopIterations);
        var expected = loopIterations == 0 ? 1.0 : 4.0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            Assert.True(pending.IsCompletedSuccessfully);
            var result = pending.Result;
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(expected),
                BitConverter.DoubleToInt64Bits(((F64Value)result.Value!).Value));
        }

        return GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    }

    private static long MeasureNested(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxValue input)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
        Assert.True(pending.IsCompletedSuccessfully);
        var result = pending.Result;
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(30_000),
            BitConverter.DoubleToInt64Bits(((F64Value)result.Value!).Value));
        Assert.Equal(40_000, result.ResourceUsage.LoopIterations);
        return GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

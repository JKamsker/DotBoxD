using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.LocalCalls.InlineI32;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class InlineI32LocalFunctionAllocationTests
{
    private const int WarmupIterations = 2_000;
    private const int MeasurementIterations = 20_000;

    [Fact]
    public async Task Inline_helpers_add_no_per_call_allocation_and_unsupported_helpers_still_fall_back()
    {
        using var host = SandboxTestHost.Create();
        var zeroPlan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.Recurrences("inline-i32-allocation-zero", 0, inlineable: true));
        var inlinePlan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.Recurrences("inline-i32-allocation-eight", 8, inlineable: true));
        var fallbackPlan = await InlineI32LocalFunctionTestSupport.PrepareAsync(
            host,
            InlineI32LocalFunctionModules.Recurrences("inline-i32-allocation-fallback", 8, inlineable: false));
        var interpreter = new SandboxInterpreter();
        var zero = MeasureCase(interpreter, zeroPlan, expected: 1, expectedFuel: 3);
        var inline = MeasureCase(interpreter, inlinePlan, expected: 9, expectedFuel: 67);
        var fallback = MeasureCase(interpreter, fallbackPlan, expected: 256, expectedFuel: 67);

        AssertBytesPerExecution(
            inline.AllocatedBytes - zero.AllocatedBytes,
            expected: 0,
            "eight inline calls");
        Assert.True(
            fallback.AllocatedBytes > inline.AllocatedBytes,
            "unsupported helpers must retain the allocating generic dispatch control");
    }

    private static Measurement MeasureCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        int expected,
        long expectedFuel)
    {
        _ = Measure(interpreter, plan, expected, expectedFuel, WarmupIterations);
        ForceGc();
        return Measure(interpreter, plan, expected, expectedFuel, MeasurementIterations);
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        int expected,
        long expectedFuel,
        int iterations)
    {
        long checksum = 0;
        var input = SandboxValue.FromInt32(1);
        var options = InlineI32LocalFunctionTestSupport.Options();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(
                plan,
                "main",
                input,
                options,
                CancellationToken.None);
            Assert.True(pending.IsCompletedSuccessfully, "allocation scenario unexpectedly became asynchronous");
            var result = pending.Result;
            Assert.True(result.Succeeded, result.Error?.SafeMessage);
            Assert.Equal(expected, InlineI32LocalFunctionTestSupport.ReadInt32(result));
            Assert.Equal(expectedFuel, result.ResourceUsage.FuelUsed);
            checksum += InlineI32LocalFunctionTestSupport.ReadInt32(result);
        }

        return new Measurement(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, checksum);
    }

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

    private readonly record struct Measurement(long AllocatedBytes, long Checksum);
}

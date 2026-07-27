using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class CompiledExecutionAlternationProbe
{
    public static CompiledEnvelopeMeasurement Measure(
        SandboxHost host,
        ExecutionPlan firstPlan,
        CompiledExecutionInvariant firstExpected,
        ExecutionPlan secondPlan,
        CompiledExecutionInvariant secondExpected,
        SandboxExecutionOptions options,
        int iterations)
    {
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var useSecond = (i & 1) != 0;
            var plan = useSecond ? secondPlan : firstPlan;
            var expected = useSecond ? secondExpected : firstExpected;
            var pending = host.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                options);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "alternating compiled execution-envelope probe unexpectedly became asynchronous");
            }

            var result = pending.Result;
            expected.ValidateSuppressedSuccess(result);
            checksum += ((I32Value)result.Value!).Value;
        }

        watch.Stop();
        var expectedChecksum = checked(7L * iterations);
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException($"expected checksum {expectedChecksum}, got {checksum}");
        }

        return new CompiledEnvelopeMeasurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }
}

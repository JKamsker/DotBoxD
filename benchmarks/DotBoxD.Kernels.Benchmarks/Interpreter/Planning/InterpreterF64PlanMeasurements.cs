using System.Diagnostics;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterF64PlanMeasurements
{
    public static Measurement Repeated(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        string entrypoint,
        SandboxValue input,
        long expectedValueBits,
        int iterations,
        int expectedHostCalls = 0,
        bool i64 = false)
    {
        long checksum = 0;
        SandboxResourceUsage? expectedUsage = null;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            var result = Execute(interpreter, plan, options, entrypoint, input);
            var valueBits = i64
                ? ((I64Value)result.Value!).Value
                : BitConverter.DoubleToInt64Bits(((F64Value)result.Value!).Value);
            if (valueBits != expectedValueBits)
            {
                throw new InvalidOperationException(
                    $"'{entrypoint}' returned bits {valueBits}, expected {expectedValueBits}.");
            }

            checksum = unchecked(checksum + valueBits);
            expectedUsage ??= result.ResourceUsage;
            if (result.ResourceUsage != expectedUsage)
            {
                throw new InvalidOperationException($"'{entrypoint}' resource usage changed between executions.");
            }
        }

        var usage = expectedUsage ?? throw new InvalidOperationException("At least one iteration is required.");
        if (usage.AllocatedBytes != 0 || usage.HostCalls != expectedHostCalls)
        {
            throw new InvalidOperationException(
                $"'{entrypoint}' sandbox allocation or host-call accounting changed.");
        }

        return new Measurement(
            Stopwatch.GetElapsedTime(startedAt),
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            usage);
    }

    public static Measurement OnceNested(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        int outerIterations,
        int innerIterations)
    {
        var input = SandboxValue.FromList(
            [
                SandboxValue.FromInt32(outerIterations),
                SandboxValue.FromInt32(innerIterations)
            ],
            SandboxType.I32);
        var expected = 1.5 * outerIterations * innerIterations;
        var measurement = Repeated(
            interpreter,
            plan,
            options,
            "nested",
            input,
            BitConverter.DoubleToInt64Bits(expected),
            iterations: 1);
        var innerExecutions = checked((long)outerIterations * innerIterations);
        var expectedFuel = checked(8L + (8L * outerIterations) + (9L * innerExecutions));
        var expectedLoops = checked(outerIterations + innerExecutions);
        if (measurement.Usage.FuelUsed != expectedFuel ||
            measurement.Usage.LoopIterations != expectedLoops ||
            measurement.Usage.CollectionElements != 2)
        {
            throw new InvalidOperationException("Nested F64 resource accounting changed.");
        }

        return measurement;
    }

    public static SandboxExecutionResult Execute(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        string entrypoint,
        SandboxValue input)
    {
        var pending = interpreter.ExecuteAsync(
            plan,
            entrypoint,
            input,
            options,
            CancellationToken.None);
        if (!pending.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException($"'{entrypoint}' unexpectedly left the synchronous path.");
        }

        var result = pending.Result;
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error?.SafeMessage ?? $"'{entrypoint}' execution failed.");
        }

        return result;
    }

    internal readonly record struct Measurement(
        TimeSpan Elapsed,
        long AllocatedBytes,
        long Checksum,
        SandboxResourceUsage Usage);
}

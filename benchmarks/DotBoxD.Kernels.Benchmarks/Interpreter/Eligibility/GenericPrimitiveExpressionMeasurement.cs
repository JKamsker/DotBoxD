using System.Diagnostics;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class GenericPrimitiveExpressionMeasurement
{
    private const int WarmupIterations = 10_000;

    public const int MeasurementIterations = 20_000;

    public const int Samples = 5;

    public static void Warm(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        object expected,
        long fuel,
        int hostCalls = 0,
        string entrypoint = "main")
        => _ = Measure(
            interpreter,
            plan,
            options,
            expected,
            fuel,
            hostCalls,
            entrypoint,
            WarmupIterations);

    public static Measurement MeasureMedian(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        object expected,
        long fuel,
        int hostCalls = 0,
        string entrypoint = "main",
        int measurementIterations = MeasurementIterations)
    {
        var measurements = new Measurement[Samples];
        for (var sample = 0; sample < Samples; sample++)
        {
            ForceGc();
            measurements[sample] = Measure(
                interpreter,
                plan,
                options,
                expected,
                fuel,
                hostCalls,
                entrypoint,
                measurementIterations);
        }

        Array.Sort(measurements, static (left, right) =>
            left.NanosecondsPerOperation.CompareTo(right.NanosecondsPerOperation));
        return measurements[measurements.Length / 2];
    }

    public static SandboxExecutionResult Execute(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        string entrypoint = "main")
    {
        var pending = interpreter.ExecuteAsync(
            plan,
            entrypoint,
            SandboxValue.Unit,
            options,
            CancellationToken.None);
        return pending.IsCompletedSuccessfully
            ? pending.Result
            : throw new InvalidOperationException("probe unexpectedly became asynchronous");
    }

    public static void RequireValue(SandboxExecutionResult result, object expected)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
        }

        var matches = (result.Value, expected) switch
        {
            (BoolValue value, bool number) => value.Value == number,
            (I32Value value, int number) => value.Value == number,
            (I64Value value, long number) => value.Value == number,
            (F64Value value, double number) => value.Value == number,
            _ => false
        };
        if (!matches)
        {
            throw new InvalidOperationException($"unexpected value {result.Value}");
        }
    }

    public static SandboxExecutionOptions Options(bool enableTrace = false)
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true,
            EnableDebugTrace = enableTrace
        };

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        object expected,
        long fuel,
        int hostCalls,
        string entrypoint,
        int iterations)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var result = Execute(interpreter, plan, options, entrypoint);
            RequireValue(result, expected);
            if (result.ResourceUsage.FuelUsed != fuel ||
                result.ResourceUsage.HostCalls != hostCalls)
            {
                throw new InvalidOperationException(
                    $"metering changed: fuel={result.ResourceUsage.FuelUsed}, " +
                    $"hosts={result.ResourceUsage.HostCalls}");
            }
        }

        watch.Stop();
        return new Measurement(
            watch.Elapsed.TotalNanoseconds / iterations,
            (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / (double)iterations,
            fuel);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    internal readonly record struct Measurement(
        double NanosecondsPerOperation,
        double BytesPerOperation,
        long Fuel);
}

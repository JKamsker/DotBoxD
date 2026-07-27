using System.Diagnostics;
using System.Globalization;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterScalarAssignmentProbe
{
    private const int WarmupIterations = 50_000;
    private const int MeasurementIterations = 100_000;

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder => builder.UseInterpreter());
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };
        var lanes = await InterpreterScalarAssignmentScenarios.PrepareAsync(host, policy);

        foreach (var lane in lanes)
        {
            foreach (var benchmarkCase in lane.Cases)
            {
                _ = Measure(
                    interpreter,
                    benchmarkCase.Plan,
                    lane.Input,
                    options,
                    lane.Type,
                    benchmarkCase.ExpectedValue,
                    benchmarkCase.ExpectedUsage,
                    WarmupIterations);
            }
        }

        Console.WriteLine($"interpreter scalar-assignment executions = {MeasurementIterations:N0}");
        Console.WriteLine(
            "case                         total ms    allocated B       B/op   incremental B/assign   checksum   F/L/A/H");
        foreach (var lane in lanes)
        {
            RunLane(interpreter, options, lane);
        }
    }

    private static void RunLane(
        SandboxInterpreter interpreter,
        SandboxExecutionOptions options,
        InterpreterScalarAssignmentLane lane)
    {
        long controlBytes = 0;
        foreach (var benchmarkCase in lane.Cases)
        {
            ForceGc();
            var measurement = Measure(
                interpreter,
                benchmarkCase.Plan,
                lane.Input,
                options,
                lane.Type,
                benchmarkCase.ExpectedValue,
                benchmarkCase.ExpectedUsage,
                MeasurementIterations);
            if (benchmarkCase.AssignmentCount == 0)
            {
                controlBytes = measurement.AllocatedBytes;
            }

            Write(lane.Name, benchmarkCase.AssignmentCount, controlBytes, measurement);
        }
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        ScalarAssignmentType type,
        double expectedValue,
        SandboxResourceUsage expectedUsage,
        int iterations)
    {
        double checksum = 0;
        var watch = new Stopwatch();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "scalar-assignment probe unexpectedly left the synchronous interpreter path");
            }

            var result = pending.Result;
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
            }

            var value = ReadValue(result.Value, type);
            if (value != expectedValue)
            {
                throw new InvalidOperationException($"expected result {expectedValue}, received {value}");
            }

            if (result.ResourceUsage != expectedUsage)
            {
                throw new InvalidOperationException(
                    $"resource usage changed: expected {expectedUsage}, received {result.ResourceUsage}");
            }

            checksum += value;
        }

        watch.Stop();
        var expectedChecksum = expectedValue * iterations;
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException($"expected checksum {expectedChecksum}, received {checksum}");
        }

        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            expectedUsage);
    }

    private static double ReadValue(SandboxValue? value, ScalarAssignmentType type)
        => (type, value) switch
        {
            (ScalarAssignmentType.I32, I32Value number) => number.Value,
            (ScalarAssignmentType.I64, I64Value number) => number.Value,
            (ScalarAssignmentType.F64, F64Value number) => number.Value,
            _ => throw new InvalidOperationException("unexpected scalar-assignment result type")
        };

    private static void Write(
        string laneName,
        int assignmentCount,
        long controlBytes,
        Measurement measurement)
    {
        var name = $"{laneName} x{assignmentCount}";
        var incrementalBytes = assignmentCount == 0
            ? "-"
            : ((measurement.AllocatedBytes - controlBytes) /
               (double)(MeasurementIterations * assignmentCount)).ToString("N1", CultureInfo.InvariantCulture);
        var usage = measurement.Usage;
        Console.WriteLine(
            $"{name,-28} {measurement.ElapsedMilliseconds,8:N1} {measurement.AllocatedBytes,14:N0} " +
            $"{measurement.AllocatedBytes / (double)MeasurementIterations,10:N1} " +
            $"{incrementalBytes,22} {measurement.Checksum,10:N0} " +
            $"{usage.FuelUsed:N0}/{usage.LoopIterations:N0}/{usage.AllocatedBytes:N0}/{usage.HostCalls:N0}");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        double ElapsedMilliseconds,
        long AllocatedBytes,
        double Checksum,
        SandboxResourceUsage Usage);

}

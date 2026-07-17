using System.Diagnostics;
using System.Globalization;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterScalarAssignmentProbe
{
    private const int WarmupIterations = 50_000;
    private const int MeasurementIterations = 100_000;
    private static readonly int[] AssignmentCounts = [0, 1, 4, 8];

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
        var lanes = new[]
        {
            await PrepareLaneAsync(host, policy, ScalarAssignmentType.I64, ScalarAssignmentRhs.Literal),
            await PrepareLaneAsync(host, policy, ScalarAssignmentType.I64, ScalarAssignmentRhs.RawVariable),
            await PrepareLaneAsync(host, policy, ScalarAssignmentType.F64, ScalarAssignmentRhs.Literal),
            await PrepareLaneAsync(host, policy, ScalarAssignmentType.F64, ScalarAssignmentRhs.RawVariable)
        };

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

    private static async Task<PreparedLane> PrepareLaneAsync(
        SandboxHost host,
        SandboxPolicy policy,
        ScalarAssignmentType type,
        ScalarAssignmentRhs rhs)
    {
        var input = CreateInput(type, rhs);
        var cases = new PreparedCase[AssignmentCounts.Length];
        for (var index = 0; index < AssignmentCounts.Length; index++)
        {
            var assignmentCount = AssignmentCounts[index];
            var moduleJson = InterpreterScalarAssignmentModules.Create(type, rhs, assignmentCount);
            var module = await host.ImportJsonAsync(moduleJson);
            cases[index] = new PreparedCase(
                assignmentCount,
                await host.PrepareAsync(module, policy),
                assignmentCount + 1D,
                ExpectedUsage(rhs, assignmentCount));
        }

        return new PreparedLane(type, rhs, input, cases);
    }

    private static void RunLane(
        SandboxInterpreter interpreter,
        SandboxExecutionOptions options,
        PreparedLane lane)
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

            Write(lane.Type, lane.Rhs, benchmarkCase.AssignmentCount, controlBytes, measurement);
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

    private static SandboxValue CreateInput(ScalarAssignmentType type, ScalarAssignmentRhs rhs)
    {
        var value = type == ScalarAssignmentType.I64
            ? SandboxValue.FromInt64(1)
            : SandboxValue.FromDouble(1);
        if (rhs == ScalarAssignmentRhs.Literal)
        {
            return value;
        }

        var step = type == ScalarAssignmentType.I64
            ? SandboxValue.FromInt64(1)
            : SandboxValue.FromDouble(1);
        var itemType = type == ScalarAssignmentType.I64 ? SandboxType.I64 : SandboxType.F64;
        return SandboxValue.FromList([value, step], itemType);
    }

    private static double ReadValue(SandboxValue? value, ScalarAssignmentType type)
        => (type, value) switch
        {
            (ScalarAssignmentType.I64, I64Value number) => number.Value,
            (ScalarAssignmentType.F64, F64Value number) => number.Value,
            _ => throw new InvalidOperationException("unexpected scalar-assignment result type")
        };

    private static SandboxResourceUsage ExpectedUsage(ScalarAssignmentRhs rhs, int assignmentCount)
        => new(
            FuelUsed: 3 + (4L * assignmentCount),
            MaxFuel: long.MaxValue,
            LoopIterations: 0,
            AllocatedBytes: 0,
            HostCalls: 0,
            FileBytesRead: 0,
            FileBytesWritten: 0,
            NetworkBytesRead: 0,
            NetworkBytesWritten: 0,
            LogEvents: 0,
            CollectionElements: rhs == ScalarAssignmentRhs.Literal ? 0 : 2,
            StringBytes: 0);

    private static void Write(
        ScalarAssignmentType type,
        ScalarAssignmentRhs rhs,
        int assignmentCount,
        long controlBytes,
        Measurement measurement)
    {
        var name = $"{type} {(rhs == ScalarAssignmentRhs.Literal ? "literal" : "raw variable")} x{assignmentCount}";
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

    private readonly record struct PreparedCase(
        int AssignmentCount,
        ExecutionPlan Plan,
        double ExpectedValue,
        SandboxResourceUsage ExpectedUsage);

    private sealed record PreparedLane(
        ScalarAssignmentType Type,
        ScalarAssignmentRhs Rhs,
        SandboxValue Input,
        PreparedCase[] Cases);
}

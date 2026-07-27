using System.Diagnostics;
using System.Globalization;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterScalarReturnProbe
{
    private const int WarmupIterations = 50_000;
    private const int MeasurementIterations = 100_000;
    private static readonly int[] OperationCounts = [0, 1, 8];

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder => builder.UseInterpreter());
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var interpreter = new SandboxInterpreter();
        var options = Options();
        var lanes = new[]
        {
            await PrepareLaneAsync(host, policy, ScalarReturnType.I64, ScalarReturnOperand.Literal),
            await PrepareLaneAsync(host, policy, ScalarReturnType.I64, ScalarReturnOperand.RawVariable),
            await PrepareLaneAsync(host, policy, ScalarReturnType.F64, ScalarReturnOperand.Literal),
            await PrepareLaneAsync(host, policy, ScalarReturnType.F64, ScalarReturnOperand.RawVariable)
        };

        foreach (var lane in lanes)
        {
            foreach (var benchmarkCase in lane.Cases)
            {
                _ = Measure(interpreter, benchmarkCase, lane, options, WarmupIterations);
            }
        }

        Console.WriteLine($"interpreter scalar-return executions = {MeasurementIterations:N0}");
        Console.WriteLine(
            "case                         total ms    allocated B       B/op   incremental B/op   checksum   F/L/A/H");
        foreach (var lane in lanes)
        {
            RunLane(interpreter, lane, options);
        }
    }

    private static async Task<PreparedLane> PrepareLaneAsync(
        SandboxHost host,
        SandboxPolicy policy,
        ScalarReturnType type,
        ScalarReturnOperand operand)
    {
        var scenario = Scenario.Create(type, operand);
        var cases = new PreparedCase[OperationCounts.Length];
        for (var index = 0; index < OperationCounts.Length; index++)
        {
            var operationCount = OperationCounts[index];
            var module = await host.ImportJsonAsync(
                InterpreterScalarReturnModules.Create(type, operand, operationCount));
            cases[index] = new PreparedCase(
                operationCount,
                await host.PrepareAsync(module, policy),
                scenario.InitialValue + (operationCount * scenario.Step),
                ExpectedUsage(operationCount, scenario.CollectionElements));
        }

        return new PreparedLane(type, operand, scenario.Input, cases);
    }

    private static void RunLane(
        SandboxInterpreter interpreter,
        PreparedLane lane,
        SandboxExecutionOptions options)
    {
        long controlBytes = 0;
        foreach (var benchmarkCase in lane.Cases)
        {
            ForceGc();
            var measurement = Measure(
                interpreter,
                benchmarkCase,
                lane,
                options,
                MeasurementIterations);
            if (benchmarkCase.OperationCount == 0)
            {
                controlBytes = measurement.AllocatedBytes;
            }

            Write(lane, benchmarkCase.OperationCount, controlBytes, measurement);
        }
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        PreparedCase benchmarkCase,
        PreparedLane lane,
        SandboxExecutionOptions options,
        int iterations)
    {
        double checksum = 0;
        var watch = new Stopwatch();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(
                benchmarkCase.Plan,
                "main",
                lane.Input,
                options,
                CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("scalar-return probe unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
            }

            var value = ReadValue(result.Value, lane.Type);
            if (value != benchmarkCase.ExpectedValue)
            {
                throw new InvalidOperationException(
                    $"expected result {benchmarkCase.ExpectedValue}, received {value}");
            }

            if (result.ResourceUsage != benchmarkCase.ExpectedUsage)
            {
                throw new InvalidOperationException(
                    $"resource usage changed: expected {benchmarkCase.ExpectedUsage}, received {result.ResourceUsage}");
            }

            checksum += value;
        }

        watch.Stop();
        var expectedChecksum = benchmarkCase.ExpectedValue * iterations;
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"expected checksum {expectedChecksum}, received {checksum}");
        }

        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            benchmarkCase.ExpectedUsage);
    }

    private static void Write(
        PreparedLane lane,
        int operationCount,
        long controlBytes,
        Measurement measurement)
    {
        var operand = lane.Operand == ScalarReturnOperand.Literal ? "literal" : "raw variable";
        var name = $"{lane.Type} {operand} x{operationCount}";
        var incremental = operationCount == 0
            ? "-"
            : ((measurement.AllocatedBytes - controlBytes) /
               (double)MeasurementIterations).ToString("N1", CultureInfo.InvariantCulture);
        var usage = measurement.Usage;
        Console.WriteLine(
            $"{name,-28} {measurement.ElapsedMilliseconds,8:N1} {measurement.AllocatedBytes,14:N0} " +
            $"{measurement.AllocatedBytes / (double)MeasurementIterations,10:N1} " +
            $"{incremental,18} {measurement.Checksum,10:N0} " +
            $"{usage.FuelUsed:N0}/{usage.LoopIterations:N0}/{usage.AllocatedBytes:N0}/{usage.HostCalls:N0}");
    }

    private static double ReadValue(SandboxValue? value, ScalarReturnType type)
        => (type, value) switch
        {
            (ScalarReturnType.I64, I64Value number) => number.Value,
            (ScalarReturnType.F64, F64Value number) => number.Value,
            _ => throw new InvalidOperationException("unexpected scalar-return result type")
        };

    private static SandboxResourceUsage ExpectedUsage(int operationCount, long collectionElements)
        => new(
            FuelUsed: 3 + (2L * operationCount),
            MaxFuel: long.MaxValue,
            LoopIterations: 0,
            AllocatedBytes: 0,
            HostCalls: 0,
            FileBytesRead: 0,
            FileBytesWritten: 0,
            NetworkBytesRead: 0,
            NetworkBytesWritten: 0,
            LogEvents: 0,
            CollectionElements: collectionElements,
            StringBytes: 0);

    private static SandboxExecutionOptions Options()
        => new()
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record Scenario(
        SandboxValue Input,
        double InitialValue,
        double Step,
        long CollectionElements)
    {
        public static Scenario Create(ScalarReturnType type, ScalarReturnOperand operand)
        {
            var initial = type == ScalarReturnType.I64 ? 2D : 0.5D;
            var step = type == ScalarReturnType.I64 ? 3D : 0.25D;
            var initialValue = Scalar(type, initial);
            return operand == ScalarReturnOperand.Literal
                ? new Scenario(initialValue, initial, step, CollectionElements: 0)
                : new Scenario(
                    SandboxValue.FromList([initialValue, Scalar(type, step)], Type(type)),
                    initial,
                    step,
                    CollectionElements: 2);
        }

        private static SandboxValue Scalar(ScalarReturnType type, double value)
            => type == ScalarReturnType.I64
                ? SandboxValue.FromInt64((long)value)
                : SandboxValue.FromDouble(value);

        private static SandboxType Type(ScalarReturnType type)
            => type == ScalarReturnType.I64 ? SandboxType.I64 : SandboxType.F64;
    }

    private sealed record PreparedLane(
        ScalarReturnType Type,
        ScalarReturnOperand Operand,
        SandboxValue Input,
        PreparedCase[] Cases);

    private sealed record PreparedCase(
        int OperationCount,
        ExecutionPlan Plan,
        double ExpectedValue,
        SandboxResourceUsage ExpectedUsage);

    private readonly record struct Measurement(
        double ElapsedMilliseconds,
        long AllocatedBytes,
        double Checksum,
        SandboxResourceUsage Usage);
}

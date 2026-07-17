using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterLocalCallArgumentProbe
{
    private const int WarmupIterations = 2_000;
    private const int Iterations = 100_000;
    private const int ExpectedValue = 7;

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder => builder.UseInterpreter());
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var plans = new PreparedPlans(
            await PrepareAsync(host, InterpreterLocalCallArgumentModules.ZeroArity(), policy),
            await PrepareAsync(host, InterpreterLocalCallArgumentModules.OneArity(), policy),
            await PrepareAsync(host, InterpreterLocalCallArgumentModules.TwoArity(), policy),
            await PrepareAsync(host, InterpreterLocalCallArgumentModules.ThreeArity(), policy));
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        Console.WriteLine($"interpreter local-call argument executions = {Iterations:N0}");
        Console.WriteLine(
            "case                       total ms    allocated B       B/op   checksum   fuel/op  loops/op sandbox B/op host/op");
        var zero = RunPair(
            interpreter,
            plans.Zero,
            SandboxValue.Unit,
            options,
            "arity 0",
            directUsage: new(3, 0, 0, 0),
            callUsage: new(8, 0, 0, 0));
        var one = RunPair(
            interpreter,
            plans.One,
            SandboxValue.FromString("a"),
            options,
            "arity 1",
            directUsage: new(3, 0, 2, 0),
            callUsage: new(9, 0, 2, 0));
        var two = RunPair(
            interpreter,
            plans.Two,
            SandboxValue.FromList(
                [SandboxValue.FromString("a"), SandboxValue.FromString("b")],
                SandboxType.String),
            options,
            "arity 2",
            directUsage: new(3, 0, 4, 0),
            callUsage: new(10, 0, 4, 0));
        var three = RunPair(
            interpreter,
            plans.Three,
            SandboxValue.FromList(
                [
                    SandboxValue.FromString("a"),
                    SandboxValue.FromString("b"),
                    SandboxValue.FromString("c")
                ],
                SandboxType.String),
            options,
            "arity 3",
            directUsage: new(3, 0, 6, 0),
            callUsage: new(11, 0, 6, 0));

        PrintDecomposition(zero, one, two, three);
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        string moduleJson,
        SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, policy);
    }

    private static PairMeasurement RunPair(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        string name,
        ResourceUsageInvariant directUsage,
        ResourceUsageInvariant callUsage)
    {
        var direct = RunCase(interpreter, plan, "direct", input, options, $"{name} direct control", directUsage);
        var call = RunCase(interpreter, plan, "call", input, options, $"{name} local call", callUsage);
        return new PairMeasurement(direct, call);
    }

    private static Measurement RunCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        string name,
        ResourceUsageInvariant expectedUsage)
    {
        _ = Measure(interpreter, plan, entrypoint, input, options, expectedUsage, WarmupIterations);
        ForceGc();
        var measurement = Measure(interpreter, plan, entrypoint, input, options, expectedUsage, Iterations);
        var expectedChecksum = (long)ExpectedValue * Iterations;
        if (measurement.Checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"{name} expected checksum {expectedChecksum}, got {measurement.Checksum}");
        }

        var usage = measurement.Usage;
        Console.WriteLine(
            $"{name,-26} {measurement.ElapsedMilliseconds,8:N1} {measurement.Bytes,14:N0} " +
            $"{measurement.Bytes / (double)Iterations,10:N1} " +
            $"{measurement.Checksum,10:N0} {usage.FuelUsed,9:N0} {usage.LoopIterations,9:N0} " +
            $"{usage.AllocatedBytes,12:N0} {usage.HostCalls,7:N0}");
        return measurement;
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        ResourceUsageInvariant expectedUsage,
        int iterations)
    {
        long checksum = 0;
        var watch = new Stopwatch();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, entrypoint, input, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("local-call argument probe unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded || result.Value is not I32Value value || value.Value != ExpectedValue)
            {
                throw new InvalidOperationException(result.Error?.SafeMessage ?? "unexpected execution result");
            }

            checksum += value.Value;
            var usage = ResourceUsageInvariant.From(result.ResourceUsage);
            if (usage != expectedUsage)
            {
                throw new InvalidOperationException(
                    $"{entrypoint} expected sandbox resource usage {expectedUsage}, got {usage}");
            }
        }

        watch.Stop();
        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            expectedUsage);
    }

    private static void PrintDecomposition(
        PairMeasurement zero,
        PairMeasurement one,
        PairMeasurement two,
        PairMeasurement three)
    {
        var fixedCallAndFrameBytes = zero.DeltaBytes;
        var oneArgumentArrayBytes = one.DeltaBytes - fixedCallAndFrameBytes;
        var twoArgumentArrayBytes = two.DeltaBytes - fixedCallAndFrameBytes;
        var threeArgumentArrayBytes = three.DeltaBytes - fixedCallAndFrameBytes;
        Console.WriteLine();
        Console.WriteLine("managed allocation decomposition per local call:");
        Console.WriteLine($"fixed dispatch + padded callee frame {PerExecution(fixedCallAndFrameBytes),10:N1} B/op");
        Console.WriteLine($"arity 1 caller argument array       {PerExecution(oneArgumentArrayBytes),10:N1} B/op");
        Console.WriteLine($"arity 2 caller argument array       {PerExecution(twoArgumentArrayBytes),10:N1} B/op");
        Console.WriteLine($"arity 3 caller argument array       {PerExecution(threeArgumentArrayBytes),10:N1} B/op");
    }

    private static double PerExecution(long bytes) => bytes / (double)Iterations;

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct ResourceUsageInvariant(
        long FuelUsed,
        long LoopIterations,
        long AllocatedBytes,
        int HostCalls)
    {
        public static ResourceUsageInvariant From(SandboxResourceUsage usage)
            => new(usage.FuelUsed, usage.LoopIterations, usage.AllocatedBytes, usage.HostCalls);
    }

    private readonly record struct Measurement(
        double ElapsedMilliseconds,
        long Bytes,
        long Checksum,
        ResourceUsageInvariant Usage);

    private readonly record struct PairMeasurement(Measurement Direct, Measurement Call)
    {
        public long DeltaBytes => Call.Bytes - Direct.Bytes;
    }

    private readonly record struct PreparedPlans(
        ExecutionPlan Zero,
        ExecutionPlan One,
        ExecutionPlan Two,
        ExecutionPlan Three);
}

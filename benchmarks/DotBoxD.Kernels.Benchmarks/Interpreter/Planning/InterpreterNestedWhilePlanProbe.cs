using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterNestedWhilePlanProbe
{
    private const int OuterIterations = 1_000_000;
    private const int WarmupIterations = 4_096;

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder => builder.UseInterpreter());
        var module = await host.ImportJsonAsync(InterpreterNestedWhilePlanModule.Json);
        var plan = await host.PrepareAsync(module, Policy());
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        _ = Measure(interpreter, plan, options, WarmupIterations, enterWhile: true);
        _ = Measure(interpreter, plan, options, WarmupIterations, enterWhile: false);

        var entered = RunCase(interpreter, plan, options, "enter zero-iteration while", enterWhile: true);
        var skipped = RunCase(interpreter, plan, options, "skip while control", enterWhile: false);

        Console.WriteLine("interpreter nested-while plan probe");
        Console.WriteLine("case                         total ms    allocated B       result");
        Write(entered);
        Write(skipped);
        Console.WriteLine(
            $"entered-minus-skipped = {entered.AllocatedBytes - skipped.AllocatedBytes:N0} B, " +
            $"{(entered.AllocatedBytes - skipped.AllocatedBytes) / (double)OuterIterations:N1} B/entry, " +
            $"{entered.ElapsedMilliseconds - skipped.ElapsedMilliseconds:N1} ms");
    }

    private static NestedWhileMeasurement RunCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        string name,
        bool enterWhile)
    {
        ForceGc();
        return Measure(interpreter, plan, options, OuterIterations, enterWhile) with { Name = name };
    }

    private static NestedWhileMeasurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        int outerIterations,
        bool enterWhile)
    {
        var input = Input(outerIterations, enterWhile);
        var expectedUsage = ExpectedUsage(outerIterations, enterWhile);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
        if (!pending.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("nested-while plan probe unexpectedly became asynchronous");
        }

        var result = pending.Result;
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
        }

        var actual = ((I32Value)result.Value!).Value;
        if (actual != outerIterations)
        {
            throw new InvalidOperationException($"expected {outerIterations}, got {actual}");
        }

        var usage = ResourceUsageInvariant.From(result.ResourceUsage);
        if (usage != expectedUsage)
        {
            throw new InvalidOperationException($"resource usage changed: expected {expectedUsage}, got {usage}");
        }

        return new NestedWhileMeasurement("", elapsed.TotalMilliseconds, allocated, actual, usage);
    }

    private static ResourceUsageInvariant ExpectedUsage(int outerIterations, bool enterWhile)
        => new(
            FuelUsed: 10L + ((enterWhile ? 17L : 13L) * outerIterations),
            LoopIterations: outerIterations,
            AllocatedBytes: 0,
            HostCalls: 0,
            CollectionElements: 2);

    private static SandboxValue Input(int outerIterations, bool enterWhile)
        => SandboxValue.FromList(
            [SandboxValue.FromInt32(outerIterations), SandboxValue.FromInt32(enterWhile ? 1 : 0)],
            SandboxType.I32);

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxTotalCollectionElements(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

    private static void Write(NestedWhileMeasurement measurement)
    {
        Console.WriteLine(
            $"{measurement.Name,-28} {measurement.ElapsedMilliseconds,8:N1} " +
            $"{measurement.AllocatedBytes,14:N0} {measurement.Result,12:N0}");
        Console.WriteLine(
            $"  usage fuel={measurement.Usage.FuelUsed:N0} " +
            $"loops={measurement.Usage.LoopIterations:N0} sandboxB={measurement.Usage.AllocatedBytes:N0} " +
            $"host={measurement.Usage.HostCalls:N0} elements={measurement.Usage.CollectionElements:N0}");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct NestedWhileMeasurement(
        string Name,
        double ElapsedMilliseconds,
        long AllocatedBytes,
        int Result,
        ResourceUsageInvariant Usage);

    private readonly record struct ResourceUsageInvariant(
        long FuelUsed,
        long LoopIterations,
        long AllocatedBytes,
        int HostCalls,
        long CollectionElements)
    {
        public static ResourceUsageInvariant From(SandboxResourceUsage usage)
            => new(
                usage.FuelUsed,
                usage.LoopIterations,
                usage.AllocatedBytes,
                usage.HostCalls,
                usage.CollectionElements);
    }
}

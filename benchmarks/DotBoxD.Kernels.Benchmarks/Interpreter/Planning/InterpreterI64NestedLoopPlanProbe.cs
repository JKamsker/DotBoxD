using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterI64NestedLoopPlanProbe
{
    private const int RepeatedOuterIterations = 1_000_000;
    private const int BodyControlInnerIterations = 10_000_000;
    private const int WarmupIterations = 4_096;

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder => builder.UseInterpreter());
        var module = await host.ImportJsonAsync(InterpreterI64NestedLoopPlanModule.Json);
        var plan = await host.PrepareAsync(module, Policy());
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };
        var scenarios = new[]
        {
            new Scenario(
                "multi body, outer 1/inner 10M",
                "multiBodyControl",
                1,
                BodyControlInnerIterations,
                BaseFuel: 10,
                InnerFuel: 13),
            new Scenario(
                "multi body, outer 1M/inner 1",
                "multiBodyControl",
                RepeatedOuterIterations,
                1,
                BaseFuel: 10,
                InnerFuel: 13),
            new Scenario(
                "outer 1M, inner 0",
                "main",
                RepeatedOuterIterations,
                0,
                BaseFuel: 8,
                InnerFuel: 9),
            new Scenario(
                "outer 1M, inner 1",
                "main",
                RepeatedOuterIterations,
                1,
                BaseFuel: 8,
                InnerFuel: 9)
        };

        foreach (var scenario in scenarios)
        {
            WarmTwice(interpreter, plan, options, scenario);
        }

        Console.WriteLine("interpreter nested-I64 loop-plan probe");
        Console.WriteLine("case                              total ms    allocated B       result      fuel        loops");
        foreach (var scenario in scenarios)
        {
            ForceGc();
            Write(Measure(interpreter, plan, options, scenario));
        }
    }

    private static void WarmTwice(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        Scenario scenario)
    {
        var warmup = scenario with
        {
            OuterIterations = Math.Min(scenario.OuterIterations, WarmupIterations),
            InnerIterations = Math.Min(scenario.InnerIterations, WarmupIterations)
        };
        _ = Measure(interpreter, plan, options, warmup);
        _ = Measure(interpreter, plan, options, warmup);
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        Scenario scenario)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        var pending = interpreter.ExecuteAsync(
            plan,
            scenario.Entrypoint,
            Input(scenario),
            options,
            CancellationToken.None);
        if (!pending.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("nested-I64 probe unexpectedly became asynchronous");
        }

        var result = pending.Result;
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
        }

        var innerExecutions = checked((long)scenario.OuterIterations * scenario.InnerIterations);
        var expectedFuel = checked(
            scenario.BaseFuel +
            (8L * scenario.OuterIterations) +
            (scenario.InnerFuel * innerExecutions));
        var expectedLoops = checked(scenario.OuterIterations + innerExecutions);
        var actual = ((I64Value)result.Value!).Value;
        if (actual != innerExecutions ||
            result.ResourceUsage.FuelUsed != expectedFuel ||
            result.ResourceUsage.LoopIterations != expectedLoops ||
            result.ResourceUsage.AllocatedBytes != 0 ||
            result.ResourceUsage.HostCalls != 0 ||
            result.ResourceUsage.CollectionElements != 2)
        {
            throw new InvalidOperationException("nested-I64 result or resource accounting changed");
        }

        return new Measurement(
            scenario.Name,
            elapsed.TotalMilliseconds,
            allocated,
            actual,
            expectedFuel,
            expectedLoops);
    }

    private static SandboxValue Input(Scenario scenario)
        => SandboxValue.FromList(
            [
                SandboxValue.FromInt32(scenario.OuterIterations),
                SandboxValue.FromInt32(scenario.InnerIterations)
            ],
            SandboxType.I32);

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxTotalCollectionElements(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-33} {measurement.ElapsedMilliseconds,9:N3} " +
            $"{measurement.AllocatedBytes,14:N0} {measurement.Result,12:N0} " +
            $"{measurement.Fuel,11:N0} {measurement.Loops,12:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Scenario(
        string Name,
        string Entrypoint,
        int OuterIterations,
        int InnerIterations,
        int BaseFuel,
        int InnerFuel);

    private readonly record struct Measurement(
        string Name,
        double ElapsedMilliseconds,
        long AllocatedBytes,
        long Result,
        long Fuel,
        long Loops);
}

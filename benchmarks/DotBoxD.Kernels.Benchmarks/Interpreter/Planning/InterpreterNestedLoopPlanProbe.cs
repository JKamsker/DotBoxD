using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterNestedLoopPlanProbe
{
    private const int MainOuterIterations = 1_000_000;
    private const int MainInnerIterations = 1;
    private const int IndexOuterIterations = 500_000;
    private const int TriangleOuterIterations = 2_000;
    private const int BodyControlInnerIterations = 10_000_000;
    private const int WarmupIterations = 4_096;
    private const int Modulus = 1_000_003;
    private const int Increment = 3;

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(InterpreterNestedLoopPlanModule.Json);
        var plan = await host.PrepareAsync(module, Policy());
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        var mainScenario = new NestedLoopScenario(
            "outer 1M, inner 1",
            "main",
            MainOuterIterations,
            MainInnerIterations,
            InnerEndFuel: 1,
            ResultFormula.FixedIncrement);
        var zeroScenario = mainScenario with
        {
            Name = "outer 1M, inner 0",
            InnerIterations = 0
        };
        var indexScenario = new NestedLoopScenario(
            "outer 500k, inner 2/index",
            "indexSensitive",
            IndexOuterIterations,
            InnerIterations: 2,
            InnerEndFuel: 1,
            ResultFormula.InnerIndex);
        var bodyControlScenario = mainScenario with
        {
            Name = "outer 1, inner 10M",
            OuterIterations = 1,
            InnerIterations = BodyControlInnerIterations
        };
        var triangleScenario = new NestedLoopScenario(
            "outer 2k, triangular/outer index",
            "outerIndexDependent",
            TriangleOuterIterations,
            InnerIterations: 0,
            InnerEndFuel: 1,
            ResultFormula.OuterIndexTriangle);
        var fallbackScenario = mainScenario with
        {
            Name = "arithmetic-bound fallback",
            Entrypoint = "unsupportedBound",
            OuterIterations = IndexOuterIterations,
            InnerEndFuel = 3
        };

        WarmTwice(interpreter, plan, options, mainScenario);
        WarmTwice(interpreter, plan, options, zeroScenario);
        WarmTwice(interpreter, plan, options, indexScenario);
        WarmTwice(interpreter, plan, options, bodyControlScenario);
        WarmTwice(interpreter, plan, options, triangleScenario);
        WarmTwice(interpreter, plan, options, fallbackScenario);

        var main = RunCase(interpreter, plan, options, mainScenario);
        var zero = RunCase(interpreter, plan, options, zeroScenario);
        var index = RunCase(interpreter, plan, options, indexScenario);
        var bodyControl = RunCase(interpreter, plan, options, bodyControlScenario);
        var triangle = RunCase(interpreter, plan, options, triangleScenario);
        var fallback = RunCase(interpreter, plan, options, fallbackScenario);

        Console.WriteLine("interpreter nested-loop plan probe");
        Console.WriteLine("case                               total ms    allocated B       result");
        Write(main);
        Write(zero);
        Write(index);
        Write(bodyControl);
        Write(triangle);
        Write(fallback);
        Console.WriteLine(
            $"main-minus-zero allocation = {main.AllocatedBytes - zero.AllocatedBytes:N0} B " +
            $"({(main.AllocatedBytes - zero.AllocatedBytes) / (double)MainOuterIterations:N1} B/inner entry)");
    }

    private static void WarmTwice(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        NestedLoopScenario scenario)
    {
        var warmup = scenario with
        {
            Name = string.Empty,
            OuterIterations = Math.Min(scenario.OuterIterations, WarmupIterations),
            InnerIterations = Math.Min(scenario.InnerIterations, WarmupIterations)
        };
        _ = Measure(interpreter, plan, options, warmup);
        _ = Measure(interpreter, plan, options, warmup);
    }

    private static NestedLoopMeasurement RunCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        NestedLoopScenario scenario)
    {
        // The optimized lanes reach later controls much sooner than the baseline.
        // After collection, run the same full-size workload immediately before each
        // sample so post-GC core/cache warm-up is outside the measured interval.
        ForceGc();
        _ = Measure(interpreter, plan, options, scenario with { Name = string.Empty });
        return Measure(interpreter, plan, options, scenario);
    }

    private static NestedLoopMeasurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        NestedLoopScenario scenario)
    {
        var input = Input(scenario.OuterIterations, scenario.InnerIterations);
        var expectedValue = ExpectedValue(scenario);
        var expectedUsage = ExpectedUsage(scenario);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        var pending = interpreter.ExecuteAsync(plan, scenario.Entrypoint, input, options, CancellationToken.None);
        if (!pending.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("nested-loop plan probe unexpectedly became asynchronous");
        }

        var result = pending.Result;
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
        }

        var actualValue = ((I32Value)result.Value!).Value;
        if (actualValue != expectedValue)
        {
            throw new InvalidOperationException($"expected {expectedValue}, got {actualValue}");
        }

        var usage = ResourceUsageInvariant.From(result.ResourceUsage);
        if (usage != expectedUsage)
        {
            throw new InvalidOperationException($"resource usage changed: expected {expectedUsage}, got {usage}");
        }

        return new NestedLoopMeasurement(
            scenario.Name,
            elapsed.TotalMilliseconds,
            allocatedBytes,
            actualValue,
            usage);
    }

    private static int ExpectedValue(NestedLoopScenario scenario)
    {
        var innerExecutions = InnerExecutions(scenario);
        var accumulated = scenario.ResultFormula switch
        {
            ResultFormula.FixedIncrement => checked(innerExecutions * Increment),
            ResultFormula.InnerIndex => checked(
                (long)scenario.OuterIterations *
                scenario.InnerIterations *
                (scenario.InnerIterations - 1) / 2),
            ResultFormula.OuterIndexTriangle => SumSquares(scenario.OuterIterations),
            _ => throw new InvalidOperationException("unknown nested-loop result formula")
        };
        return (int)(accumulated % Modulus);
    }

    private static ResourceUsageInvariant ExpectedUsage(NestedLoopScenario scenario)
    {
        var innerExecutions = InnerExecutions(scenario);
        var outerFuel = 7L + scenario.InnerEndFuel;
        var fuel = checked(8L + (outerFuel * scenario.OuterIterations) + (11L * innerExecutions));
        var loops = checked(scenario.OuterIterations + innerExecutions);
        return new ResourceUsageInvariant(
            fuel,
            long.MaxValue,
            loops,
            AllocatedBytes: 0,
            HostCalls: 0,
            FileBytesRead: 0,
            FileBytesWritten: 0,
            NetworkBytesRead: 0,
            NetworkBytesWritten: 0,
            LogEvents: 0,
            CollectionElements: 2,
            StringBytes: 0);
    }

    private static long InnerExecutions(NestedLoopScenario scenario)
        => scenario.ResultFormula == ResultFormula.OuterIndexTriangle
            ? checked((long)scenario.OuterIterations * (scenario.OuterIterations - 1) / 2)
            : checked((long)scenario.OuterIterations * scenario.InnerIterations);

    private static long SumSquares(int exclusiveEnd)
        => checked((long)exclusiveEnd * (exclusiveEnd - 1) * ((2L * exclusiveEnd) - 1) / 6);

    private static SandboxValue Input(int outerIterations, int innerIterations)
        => SandboxValue.FromList(
            [SandboxValue.FromInt32(outerIterations), SandboxValue.FromInt32(innerIterations)],
            SandboxType.I32);

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxTotalCollectionElements(long.MaxValue)
            .WithMaxTotalStringBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

    private static void Write(NestedLoopMeasurement measurement)
    {
        Console.WriteLine(
            $"{measurement.Name,-34} {measurement.ElapsedMilliseconds,10:N3} " +
            $"{measurement.AllocatedBytes,14:N0} {measurement.Result,12:N0}");
        Console.WriteLine(
            $"  usage fuel={measurement.Usage.FuelUsed:N0}/{measurement.Usage.MaxFuel:N0} " +
            $"loops={measurement.Usage.LoopIterations:N0} sandboxB={measurement.Usage.AllocatedBytes:N0} " +
            $"host={measurement.Usage.HostCalls:N0} file={measurement.Usage.FileBytesRead:N0}/" +
            $"{measurement.Usage.FileBytesWritten:N0} network={measurement.Usage.NetworkBytesRead:N0}/" +
            $"{measurement.Usage.NetworkBytesWritten:N0} logs={measurement.Usage.LogEvents:N0} " +
            $"elements={measurement.Usage.CollectionElements:N0} strings={measurement.Usage.StringBytes:N0}");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct NestedLoopMeasurement(
        string Name,
        double ElapsedMilliseconds,
        long AllocatedBytes,
        int Result,
        ResourceUsageInvariant Usage);

    private readonly record struct NestedLoopScenario(
        string Name,
        string Entrypoint,
        int OuterIterations,
        int InnerIterations,
        int InnerEndFuel,
        ResultFormula ResultFormula);

    private enum ResultFormula
    {
        FixedIncrement,
        InnerIndex,
        OuterIndexTriangle,
    }
}

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

        Warm(interpreter, plan, options, WarmupIterations, MainInnerIterations);
        Warm(interpreter, plan, options, WarmupIterations, innerIterations: 0);
        Warm(
            interpreter,
            plan,
            options,
            outerIterations: 1,
            innerIterations: WarmupIterations);

        var main = RunCase(
            interpreter,
            plan,
            options,
            "outer 1M, inner 1",
            MainOuterIterations,
            MainInnerIterations);
        var zero = RunCase(
            interpreter,
            plan,
            options,
            "outer 1M, inner 0",
            MainOuterIterations,
            innerIterations: 0);
        var amortized = RunCase(
            interpreter,
            plan,
            options,
            "outer 1, inner 1M",
            outerIterations: 1,
            innerIterations: MainOuterIterations);

        Console.WriteLine("interpreter nested-loop plan probe");
        Console.WriteLine("case                         total ms    allocated B       result");
        Write(main);
        Write(zero);
        Write(amortized);
        Console.WriteLine(
            $"main-minus-zero allocation = {main.AllocatedBytes - zero.AllocatedBytes:N0} B " +
            $"({(main.AllocatedBytes - zero.AllocatedBytes) / (double)MainOuterIterations:N1} B/inner entry)");
    }

    private static void Warm(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        int outerIterations,
        int innerIterations)
        => _ = Measure(interpreter, plan, options, outerIterations, innerIterations);

    private static NestedLoopMeasurement RunCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        string name,
        int outerIterations,
        int innerIterations)
    {
        ForceGc();
        var measurement = Measure(interpreter, plan, options, outerIterations, innerIterations);
        return measurement with { Name = name };
    }

    private static NestedLoopMeasurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        int outerIterations,
        int innerIterations)
    {
        var input = Input(outerIterations, innerIterations);
        var expectedIterations = checked((long)outerIterations * innerIterations);
        var expectedValue = (int)(expectedIterations * Increment % Modulus);
        var expectedUsage = ExpectedUsage(outerIterations, innerIterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
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

        return new NestedLoopMeasurement("", elapsed.TotalMilliseconds, allocatedBytes, actualValue, usage);
    }

    private static ResourceUsageInvariant ExpectedUsage(int outerIterations, int innerIterations)
    {
        var innerExecutions = checked((long)outerIterations * innerIterations);
        var fuel = checked(8L + (8L * outerIterations) + (11L * innerExecutions));
        var loops = checked(outerIterations + innerExecutions);
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
            $"{measurement.Name,-28} {measurement.ElapsedMilliseconds,8:N1} " +
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
}

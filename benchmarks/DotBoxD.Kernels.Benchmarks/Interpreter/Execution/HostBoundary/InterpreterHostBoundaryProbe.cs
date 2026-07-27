using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterHostBoundaryProbe
{
    private const int WarmupIterations = 2_000;
    private const int Iterations = 50_000;
    private static readonly SandboxExecutionOptions SuppressedOptions = new()
    {
        Mode = ExecutionMode.Interpreted,
        AllowFallbackToInterpreter = false,
        SuppressSuccessfulRunSummaryAudit = true
    };

    public static async Task RunAsync()
    {
        using var builtInHost = CreateHost();
        using var forwardingHost = CreateHost(new ForwardingSandboxInterpreter());
        var policy = CreatePolicy();
        var builtInPlan = await PrepareAsync(builtInHost, policy);
        var forwardingPlan = await PrepareAsync(forwardingHost, policy);
        var directInterpreter = new SandboxInterpreter();
        var builtInInvariant = new InterpreterHostBoundaryInvariant(builtInPlan);
        var forwardingInvariant = new InterpreterHostBoundaryInvariant(forwardingPlan);

        _ = MeasureHost(builtInHost, builtInPlan, builtInInvariant, WarmupIterations);
        _ = MeasureDirect(directInterpreter, builtInPlan, builtInInvariant, WarmupIterations);
        _ = MeasureHost(forwardingHost, forwardingPlan, forwardingInvariant, WarmupIterations);

        ForceGc();
        var direct = MeasureDirect(directInterpreter, builtInPlan, builtInInvariant, Iterations);
        ForceGc();
        var forwarding = MeasureHost(forwardingHost, forwardingPlan, forwardingInvariant, Iterations);
        ForceGc();
        var builtIn = MeasureHost(builtInHost, builtInPlan, builtInInvariant, Iterations);

        WriteResults(builtIn, direct, forwarding);
    }

    private static SandboxHost CreateHost(ISandboxInterpreter? interpreter = null)
        => SandboxHost.Create(builder => builder.UseInterpreter(interpreter));

    private static SandboxPolicy CreatePolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

    private static async Task<ExecutionPlan> PrepareAsync(SandboxHost host, SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(InterpreterAuditEnvelopeModules.PureSuccess);
        var plan = await host.PrepareAsync(module, policy);
        if (!plan.BindingReferences.TryGetValue("main", out var bindings) || bindings.Count != 0)
        {
            throw new InvalidOperationException("host-boundary plan unexpectedly references a binding");
        }

        return plan;
    }

    private static InterpreterHostBoundaryMeasurement MeasureHost(
        SandboxHost host,
        ExecutionPlan plan,
        InterpreterHostBoundaryInvariant invariant,
        int iterations)
    {
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var pending = host.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                SuppressedOptions,
                CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("interpreter host-boundary probe unexpectedly became asynchronous");
            }

            checksum += invariant.Validate(pending.Result);
        }

        return FinishMeasurement(watch, allocatedBefore, checksum, iterations);
    }

    private static InterpreterHostBoundaryMeasurement MeasureDirect(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        InterpreterHostBoundaryInvariant invariant,
        int iterations)
    {
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(
                plan,
                "main",
                SandboxValue.Unit,
                SuppressedOptions,
                CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("direct interpreter control unexpectedly became asynchronous");
            }

            checksum += invariant.Validate(pending.Result);
        }

        return FinishMeasurement(watch, allocatedBefore, checksum, iterations);
    }

    private static InterpreterHostBoundaryMeasurement FinishMeasurement(
        Stopwatch watch,
        long allocatedBefore,
        long checksum,
        int iterations)
    {
        watch.Stop();
        var expectedChecksum = checked(7L * iterations);
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException($"expected checksum {expectedChecksum}, got {checksum}");
        }

        return new InterpreterHostBoundaryMeasurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    private static void WriteResults(
        InterpreterHostBoundaryMeasurement builtIn,
        InterpreterHostBoundaryMeasurement direct,
        InterpreterHostBoundaryMeasurement forwarding)
    {
        Console.WriteLine($"interpreter host-boundary executions = {Iterations:N0}");
        Console.WriteLine("case                         total ms      ns/op    allocated B       B/op   checksum");
        Write("built-in public host", builtIn);
        Write("direct built-in control", direct);
        Write("forwarding custom host", forwarding);
        Console.WriteLine(
            $"built-in boundary overhead = " +
            $"{builtIn.NanosecondsPerOperation(Iterations) - direct.NanosecondsPerOperation(Iterations):N1} ns/op, " +
            $"{builtIn.BytesPerOperation(Iterations) - direct.BytesPerOperation(Iterations):N1} B/op");
        Console.WriteLine(
            $"forwarding boundary overhead = " +
            $"{forwarding.NanosecondsPerOperation(Iterations) - direct.NanosecondsPerOperation(Iterations):N1} ns/op, " +
            $"{forwarding.BytesPerOperation(Iterations) - direct.BytesPerOperation(Iterations):N1} B/op");
        Console.WriteLine(
            "controls = exact result envelope and plan hashes, empty audit, all twelve resource counters, synchronous completion");
    }

    private static void Write(string name, InterpreterHostBoundaryMeasurement measurement)
        => Console.WriteLine(
            $"{name,-28} {measurement.ElapsedMilliseconds,8:N1} " +
            $"{measurement.NanosecondsPerOperation(Iterations),10:N1} " +
            $"{measurement.AllocatedBytes,14:N0} {measurement.BytesPerOperation(Iterations),10:N1} " +
            $"{measurement.Checksum,10:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

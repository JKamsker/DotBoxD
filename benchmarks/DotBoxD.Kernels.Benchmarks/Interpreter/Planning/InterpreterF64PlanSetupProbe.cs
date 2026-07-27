using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using static DotBoxD.Kernels.Benchmarks.Interpreter.InterpreterF64PlanMeasurements;
using F64Measurement = DotBoxD.Kernels.Benchmarks.Interpreter.InterpreterF64PlanMeasurements.Measurement;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterF64PlanSetupProbe
{
    private const int WarmupIterations = 2_000;
    private const int SetupIterations = 50_000;
    private const int RepeatedOuterIterations = 1_000_000;
    private const int BodyControlIterations = 10_000_000;

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(InterpreterF64PlanSetupModule.Json);
        var plan = await host.PrepareAsync(module, Policy());
        var options = Options();
        var interpreter = new SandboxInterpreter();

        Console.WriteLine("Interpreter F64 plan-cache probe");
        Console.WriteLine("Targets: raw/literal/nested allocation must fall; median time must not regress by >5%.");
        Console.WriteLine("Controls: zero/intrinsic/I64 allocation exact; every control median time <= baseline +5%.");
        Console.WriteLine("case                              iterations   total ms      ns/op    allocated B      B/op checksum fuel loops host");

        RunRepeated(interpreter, plan, options, "raw, one loop", "rawSetup", 1, 4.0);
        RunRepeated(interpreter, plan, options, "raw, zero loop control", "rawSetup", 0, 1.0);
        RunRepeated(interpreter, plan, options, "literal, one loop", "literalSetup", 1, 3.25);
        RunRepeated(
            interpreter,
            plan,
            options,
            "intrinsic fallback control",
            "intrinsicControl",
            1,
            2.0,
            expectedHostCalls: 1);
        RunRepeatedI64(interpreter, plan, options);

        WarmNested(interpreter, plan, options);
        Write("nested outer 1M / inner 1", 1, OnceNested(
            interpreter, plan, options, RepeatedOuterIterations, 1));
        Write("nested outer 1M / inner 0", 1, OnceNested(
            interpreter, plan, options, RepeatedOuterIterations, 0));
        Write("nested outer 1 / inner 10M", 1, OnceNested(
            interpreter, plan, options, 1, BodyControlIterations));

        RunAdmissionStages(plan, options);
    }

    private static void RunRepeated(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        string name,
        string entrypoint,
        int loopIterations,
        double expected,
        int expectedHostCalls = 0)
    {
        var input = SandboxValue.FromInt32(loopIterations);
        _ = Repeated(
            interpreter,
            plan,
            options,
            entrypoint,
            input,
            BitConverter.DoubleToInt64Bits(expected),
            WarmupIterations,
            expectedHostCalls);
        ForceGc();
        Write(name, SetupIterations, Repeated(
            interpreter,
            plan,
            options,
            entrypoint,
            input,
            BitConverter.DoubleToInt64Bits(expected),
            SetupIterations,
            expectedHostCalls));
    }

    private static void RunRepeatedI64(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options)
    {
        var input = SandboxValue.FromInt32(1);
        _ = Repeated(
            interpreter,
            plan,
            options,
            "i64Control",
            input,
            4,
            WarmupIterations,
            i64: true);
        ForceGc();
        Write("cached I64 control", SetupIterations, Repeated(
            interpreter,
            plan,
            options,
            "i64Control",
            input,
            4,
            SetupIterations,
            i64: true));
    }

    private static void WarmNested(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options)
    {
        _ = OnceNested(interpreter, plan, options, 2, 1);
        _ = OnceNested(interpreter, plan, options, 2, 1);
    }

    private static void RunAdmissionStages(
        ExecutionPlan plan,
        SandboxExecutionOptions options)
    {
        var interpreter = new SandboxInterpreter();
        _ = Repeated(
            interpreter,
            plan,
            options,
            "rawSetup",
            SandboxValue.FromInt32(0),
            BitConverter.DoubleToInt64Bits(1.0),
            iterations: 1);
        ForceGc();

        for (var stage = 1; stage <= 3; stage++)
        {
            Write($"admission stage {stage}", 1, Repeated(
                interpreter,
                plan,
                options,
                "rawSetup",
                SandboxValue.FromInt32(1),
                BitConverter.DoubleToInt64Bits(4.0),
                iterations: 1));
        }
    }

    private static void Write(string name, int iterations, F64Measurement measurement)
    {
        var usage = measurement.Usage;
        Console.WriteLine(
            $"{name,-33} {iterations,10:N0} {measurement.Elapsed.TotalMilliseconds,10:N3} " +
            $"{measurement.Elapsed.TotalNanoseconds / iterations,10:N2} {measurement.AllocatedBytes,14:N0} " +
            $"{measurement.AllocatedBytes / (double)iterations,9:N2} {measurement.Checksum,8:N0} " +
            $"{usage.FuelUsed,4:N0} {usage.LoopIterations,5:N0} {usage.HostCalls,4:N0}");
    }

    private static SandboxPolicy Policy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithMaxTotalCollectionElements(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

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
}

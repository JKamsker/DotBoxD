using System.Diagnostics;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterBranchedPlanSetupProbe
{
    private const int WarmupIterations = 2_000;
    private const int Iterations = 50_000;

    public static async Task RunAsync()
    {
        using var host = SandboxHost.Create(builder => builder.UseInterpreter());
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var plans = await PreparePlansAsync(host, policy);
        var interpreter = new SandboxInterpreter();
        var options = new SandboxExecutionOptions
        {
            Mode = ExecutionMode.Interpreted,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = true
        };

        Console.WriteLine($"interpreter branched plan-setup executions = {Iterations:N0}");
        Console.WriteLine(
            "case                         total ms    allocated B       B/op   checksum   fuel/op  loops/op sandbox B/op host/op");
        RunNumericCases(interpreter, plans.I32, options, "I32");
        RunNumericCases(interpreter, plans.F64, options, "F64");
    }

    private static async Task<PreparedPlans> PreparePlansAsync(SandboxHost host, SandboxPolicy policy)
    {
        return new PreparedPlans(
            await PrepareAsync(host, InterpreterBranchedPlanSetupModules.OneAssignment("i32"), policy),
            await PrepareAsync(host, InterpreterBranchedPlanSetupModules.NoBranch("i32"), policy),
            await PrepareAsync(host, InterpreterBranchedPlanSetupModules.EmptyBranch("i32"), policy),
            await PrepareAsync(host, InterpreterBranchedPlanSetupModules.TwoAssignments("i32"), policy),
            await PrepareAsync(host, InterpreterBranchedPlanSetupModules.OneAssignment("f64"), policy),
            await PrepareAsync(host, InterpreterBranchedPlanSetupModules.NoBranch("f64"), policy),
            await PrepareAsync(host, InterpreterBranchedPlanSetupModules.EmptyBranch("f64"), policy),
            await PrepareAsync(host, InterpreterBranchedPlanSetupModules.TwoAssignments("f64"), policy));
    }

    private static async Task<ExecutionPlan> PrepareAsync(
        SandboxHost host,
        string moduleJson,
        SandboxPolicy policy)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, policy);
    }

    private static void RunNumericCases(
        SandboxInterpreter interpreter,
        NumericPlans plans,
        SandboxExecutionOptions options,
        string type)
    {
        RunCase(
            interpreter, plans.OneAssignment, options, $"{type} one assignment, one loop",
            input: 1, expectedValue: 3, expectedChecksum: 150_000, expectedUsage: new(23, 1, 0, 0));
        RunCase(
            interpreter, plans.OneAssignment, options, $"{type} one assignment, zero loop",
            input: 0, expectedValue: 1, expectedChecksum: 50_000, expectedUsage: new(8, 0, 0, 0));
        RunCase(
            interpreter, plans.NoBranch, options, $"{type} no-branch control",
            input: 1, expectedValue: 3, expectedChecksum: 150_000, expectedUsage: new(17, 1, 0, 0));
        RunCase(
            interpreter, plans.EmptyBranch, options, $"{type} empty-branch control",
            input: 1, expectedValue: 1, expectedChecksum: 50_000, expectedUsage: new(19, 1, 0, 0));
        RunCase(
            interpreter, plans.TwoAssignments, options, $"{type} two-assignment control",
            input: 1, expectedValue: 6, expectedChecksum: 300_000, expectedUsage: new(29, 1, 0, 0));
    }

    private static void RunCase(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        string name,
        int input,
        long expectedValue,
        long expectedChecksum,
        ResourceUsageInvariant expectedUsage)
    {
        var value = SandboxValue.FromInt32(input);
        _ = Measure(interpreter, plan, value, options, expectedValue, expectedUsage, WarmupIterations);
        ForceGc();
        var measurement = Measure(interpreter, plan, value, options, expectedValue, expectedUsage, Iterations);
        if (measurement.Checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"{name} expected checksum {expectedChecksum}, got {measurement.Checksum}");
        }

        var usage = measurement.Usage;
        Console.WriteLine(
            $"{name,-28} {measurement.ElapsedMilliseconds,8:N1} {measurement.Bytes,14:N0} " +
            $"{measurement.Bytes / (double)Iterations,10:N1} " +
            $"{measurement.Checksum,10:N0} {usage.FuelUsed,9:N0} {usage.LoopIterations,9:N0} " +
            $"{usage.AllocatedBytes,12:N0} {usage.HostCalls,7:N0}");
    }

    private static Measurement Measure(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input,
        SandboxExecutionOptions options,
        long expectedValue,
        ResourceUsageInvariant expectedUsage,
        int iterations)
    {
        long checksum = 0;
        var watch = new Stopwatch();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (var i = 0; i < iterations; i++)
        {
            var pending = interpreter.ExecuteAsync(plan, "main", input, options, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("branched plan-setup probe unexpectedly became asynchronous");
            }

            var result = pending.Result;
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
            }

            var actual = ReadIntegralValue(result.Value!);
            if (actual != expectedValue)
            {
                throw new InvalidOperationException($"expected {expectedValue}, got {actual}");
            }

            checksum += actual;
            var usage = ResourceUsageInvariant.From(result.ResourceUsage);
            if (usage != expectedUsage)
            {
                throw new InvalidOperationException(
                    $"expected sandbox resource usage {expectedUsage}, got {usage}");
            }
        }

        watch.Stop();
        return new Measurement(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            expectedUsage);
    }

    private static long ReadIntegralValue(SandboxValue value)
        => value switch
        {
            I32Value i32 => i32.Value,
            F64Value f64 when f64.Value == Math.Truncate(f64.Value) => checked((long)f64.Value),
            _ => throw new InvalidOperationException("probe expected an integral I32 or F64 result")
        };

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

    private readonly record struct NumericPlans(
        ExecutionPlan OneAssignment,
        ExecutionPlan NoBranch,
        ExecutionPlan EmptyBranch,
        ExecutionPlan TwoAssignments);

    private readonly record struct PreparedPlans(
        ExecutionPlan I32OneAssignment,
        ExecutionPlan I32NoBranch,
        ExecutionPlan I32EmptyBranch,
        ExecutionPlan I32TwoAssignments,
        ExecutionPlan F64OneAssignment,
        ExecutionPlan F64NoBranch,
        ExecutionPlan F64EmptyBranch,
        ExecutionPlan F64TwoAssignments)
    {
        public NumericPlans I32 => new(I32OneAssignment, I32NoBranch, I32EmptyBranch, I32TwoAssignments);

        public NumericPlans F64 => new(F64OneAssignment, F64NoBranch, F64EmptyBranch, F64TwoAssignments);
    }
}

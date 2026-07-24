using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class InterpreterI64PlanAdmissionProbe
{
    public static void Run(
        ExecutionPlan plan,
        SandboxExecutionOptions options)
    {
        var interpreter = new SandboxInterpreter();
        _ = Execute(interpreter, plan, options, input: 0, expectedValue: 0);
        ForceGc();

        var observed = Execute(interpreter, plan, options, input: 1, expectedValue: 8);
        var published = Execute(interpreter, plan, options, input: 1, expectedValue: 8);
        var cached = Execute(interpreter, plan, options, input: 1, expectedValue: 8);

        Console.WriteLine();
        Console.WriteLine("two-assignment I64 plan admission (layout prewarmed)");
        Console.WriteLine("stage                         allocated B   fuel   loops");
        Write("invocation 1: observe", observed);
        Write("invocation 2: publish", published);
        Write("invocation 3: cache hit", cached);
    }

    private static StageMeasurement Execute(
        SandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        int input,
        long expectedValue)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var pending = interpreter.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(input),
            options,
            CancellationToken.None);
        if (!pending.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("I64 plan-admission probe unexpectedly became asynchronous");
        }

        var result = pending.Result;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (!result.Succeeded ||
            ((I64Value)result.Value!).Value != expectedValue ||
            result.ResourceUsage.AllocatedBytes != 0 ||
            result.ResourceUsage.HostCalls != 0)
        {
            throw new InvalidOperationException(
                result.Error?.SafeMessage ?? "I64 plan-admission result or resources changed");
        }

        return new StageMeasurement(
            allocated,
            result.ResourceUsage.FuelUsed,
            result.ResourceUsage.LoopIterations);
    }

    private static void Write(string stage, StageMeasurement measurement)
        => Console.WriteLine(
            $"{stage,-29} {measurement.AllocatedBytes,11:N0} " +
            $"{measurement.FuelUsed,6:N0} {measurement.LoopIterations,7:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct StageMeasurement(
        long AllocatedBytes,
        long FuelUsed,
        long LoopIterations);
}

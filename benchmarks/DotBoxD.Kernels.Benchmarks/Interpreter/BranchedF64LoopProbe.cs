using System.Diagnostics;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class BranchedF64LoopProbe
{
    private const int WarmupIterations = 100_000;
    private const int Iterations = 5_000_000;
    private const int Samples = 7;

    public static async Task RunAsync()
    {
        var host = Hosting.Execution.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxTotalCollectionElements(long.MaxValue)
            .WithMaxTotalStringBytes(long.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

        var equalModule = await host.ImportJsonAsync(PerformanceMatrixControlFlowCases.BranchedF64LoopJson());
        var unequalModule = await host.ImportJsonAsync(
            PerformanceMatrixControlFlowCases.UnequalFuelBranchedF64LoopJson());
        var equalPlan = await host.PrepareAsync(equalModule, policy);
        var unequalPlan = await host.PrepareAsync(unequalModule, policy);

        _ = await RunSandbox(host, equalPlan, WarmupIterations, fuelPerIteration: 17);
        _ = await RunSandbox(host, unequalPlan, WarmupIterations, fuelPerIteration: 15);
        await MeasureAsync(host, equalPlan, "equal-cost branches", fuelPerIteration: 17);
        await MeasureAsync(host, unequalPlan, "unequal-cost control", fuelPerIteration: 15);
    }

    private static async Task MeasureAsync(
        Hosting.Execution.SandboxHost host,
        ExecutionPlan plan,
        string name,
        int fuelPerIteration)
    {
        var samples = new double[Samples];
        for (var i = 0; i < samples.Length; i++)
        {
            ForceGc();
            samples[i] = await TimeAsync(
                () => RunSandbox(host, plan, Iterations, fuelPerIteration));
        }

        Array.Sort(samples);
        Console.WriteLine($"branched f64 {name} iterations = {Iterations:N0}");
        Console.WriteLine($"samples = {Samples:N0}");
        Console.WriteLine($"min = {samples[0]:N1} ms");
        Console.WriteLine($"median = {samples[samples.Length / 2]:N1} ms");
        Console.WriteLine($"max = {samples[^1]:N1} ms");
    }

    private static async Task<SandboxValue?> RunSandbox(
        Hosting.Execution.SandboxHost host,
        ExecutionPlan plan,
        int iterations,
        int fuelPerIteration)
    {
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted, AllowFallbackToInterpreter = false });
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
        }

        var expectedFuel = checked(8L + ((long)fuelPerIteration * iterations));
        if (result.Value is not F64Value { Value: 1.0 } ||
            result.ResourceUsage is not
            {
                FuelUsed: var fuel,
                LoopIterations: var loopIterations,
                AllocatedBytes: 0,
                HostCalls: 0
            } ||
            fuel != expectedFuel ||
            loopIterations != iterations)
        {
            throw new InvalidOperationException("branched F64 result or resource usage changed");
        }

        return result.Value;
    }

    private static async Task<double> TimeAsync(Func<Task<SandboxValue?>> action)
    {
        var sw = Stopwatch.StartNew();
        GC.KeepAlive(await action());
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}

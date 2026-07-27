using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Benchmarks.Examples;

using System.Diagnostics;
using DotBoxD.Kernels;

internal static class PreparedValueProbe
{
    public static async Task RunAsync()
    {
        const int warmup = 2_000;
        const int iterations = 200_000;
        var e = new ExampleWorkflowProbe.DamageEvent("ice", 120, "player-1");
        var compiled = await MeasureShouldHandleMissAsync(warmup, iterations, e, ExecutionMode.Compiled);
        var auto = await MeasureShouldHandleMissAsync(warmup, iterations, e, ExecutionMode.Auto);

        Console.WriteLine("case                         iterations   elapsed     allocated/op      handled");
        WriteSummary("compiled no-audit miss", iterations, compiled);
        WriteSummary("auto compiled miss", iterations, auto);
    }

    private static async Task<RunSummary> MeasureShouldHandleMissAsync(
        int warmup,
        int iterations,
        ExampleWorkflowProbe.DamageEvent e,
        ExecutionMode mode)
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            new InMemoryPluginMessageSink(),
            defaultPolicy: MessageWritePolicy(),
            executionMode: mode);
        var kernel = await server.InstallJsonAsync(ExampleWorkflowProbe.FireDamagePackageJson());
        var adapter = ExampleWorkflowProbe.DamageEventAdapter.Instance;

        for (var i = 0; i < warmup; i++)
        {
            _ = await kernel.ShouldHandleAsync(adapter, e);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var handled = 0;
        for (var i = 0; i < iterations; i++)
        {
            var pending = kernel.ShouldHandleAsync(adapter, e);
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Warmed prepared execution did not complete synchronously.");
            }

            if (pending.GetAwaiter().GetResult())
            {
                handled++;
            }
        }

        sw.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var observation = kernel.ExecutionObservations[^1];
        if (handled != 0 ||
            observation.RequestedMode != mode ||
            observation.ActualMode != ExecutionMode.Compiled ||
            !observation.Succeeded ||
            observation.FallbackReason is not null ||
            string.IsNullOrWhiteSpace(observation.ArtifactHash))
        {
            throw new InvalidOperationException("Prepared execution probe invariants changed.");
        }

        return new RunSummary(sw.Elapsed.TotalMilliseconds, allocatedBytes, handled);
    }

    private static void WriteSummary(string name, int iterations, RunSummary summary)
        => Console.WriteLine(
            $"{name,-24} {iterations,10:N0} {summary.Milliseconds,8:N1} ms {summary.AllocatedBytes / (double)iterations,12:N1} B {summary.Handled,12:N0}");

    private static SandboxPolicy MessageWritePolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(long.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithMaxTotalStringBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

    private readonly record struct RunSummary(double Milliseconds, long AllocatedBytes, int Handled);
}

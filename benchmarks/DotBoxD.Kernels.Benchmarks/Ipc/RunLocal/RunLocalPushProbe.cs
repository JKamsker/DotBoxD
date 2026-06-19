namespace DotBoxD.Kernels.Benchmarks.Ipc.RunLocal;

using System.Diagnostics;

/// <summary>
/// Lightweight standalone yardstick for the remote <c>RunLocal</c> push path (issue #60), invoked via
/// <c>--probe-runlocal-push</c>. Reports per-case wall-clock and allocated bytes for the encode-half and the
/// decode-half so each optimization phase's effect is observable without a full BenchmarkDotNet run.
/// </summary>
internal static class RunLocalPushProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 200_000;

    public static void Run()
    {
        Console.WriteLine("RunLocal push path — allocation probe (encode-half vs decode-half)");
        Console.WriteLine(new string('-', 78));
        Console.WriteLine($"{"Case",-12} {"Half",-8} {"ms",10} {"B/op",12}");
        Console.WriteLine(new string('-', 78));

        foreach (var scenario in Enum.GetValues<RunLocalPushCase>())
        {
            Measure(scenario, "encode", static s => { _ = s.Encode(); });
            Measure(scenario, "decode", static s => s.DecodeInvokeAsync().GetAwaiter().GetResult());
            Measure(scenario, "decode-gen", static s => s.DecodeInvokeGeneratedAsync().GetAwaiter().GetResult());
        }
    }

    private static void Measure(RunLocalPushCase scenario, string half, Action<RunLocalPushScenario> action)
    {
        var instance = RunLocalPushScenario.Create(scenario);

        for (var i = 0; i < Warmup; i++)
        {
            action(instance);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < Iterations; i++)
        {
            action(instance);
        }

        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(instance.Checksum);

        var perOp = (double)allocated / Iterations;
        Console.WriteLine($"{scenario,-12} {half,-8} {watch.Elapsed.TotalMilliseconds,10:N1} {perOp,12:N1}");
    }
}

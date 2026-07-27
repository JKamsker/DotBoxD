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
    private const int DispatchControlIterations = 2_000_000;

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

        MeasureDispatchControl();
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

    private static void MeasureDispatchControl()
    {
        var scenario = new RunLocalDispatchControl();
        MeasureDispatchControl(scenario, "registry-target", scenario.DispatchAsync, Warmup, writeResult: false);
        MeasureDispatchControl(scenario, "direct-control", scenario.InvokeDirectAsync, Warmup, writeResult: false);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Console.WriteLine();
        Console.WriteLine("Generated Int32 dispatch target vs direct decoder+handler control");
        Console.WriteLine(new string('-', 78));
        MeasureDispatchControl(scenario, "registry-target", scenario.DispatchAsync, DispatchControlIterations);
        MeasureDispatchControl(scenario, "direct-control", scenario.InvokeDirectAsync, DispatchControlIterations);
    }

    private static void MeasureDispatchControl(
        RunLocalDispatchControl scenario,
        string name,
        Func<ValueTask> dispatch,
        int iterations,
        bool writeResult = true)
    {
        var initialChecksum = scenario.Checksum;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            dispatch().GetAwaiter().GetResult();
        }

        watch.Stop();
        var checksum = scenario.Checksum - initialChecksum;
        if (checksum != RunLocalDispatchControl.ExpectedChecksum(iterations))
        {
            throw new InvalidOperationException($"Dispatch control checksum mismatch: {checksum}.");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (!writeResult)
        {
            return;
        }

        Console.WriteLine(
            $"{name,-16} {watch.Elapsed.TotalMilliseconds,10:N1} ms, " +
            $"{watch.Elapsed.TotalNanoseconds / iterations,8:N1} ns/op, " +
            $"{allocated,6:N0} B, {(double)allocated / iterations,8:N1} B/op");
    }
}

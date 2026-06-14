namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;
using DotBoxD.Kernels.Runtime;

internal static class RuntimeTypeProbe
{
    public static void Run()
    {
        const int iterations = 2_000_000;
        const int warmup = 100_000;

        _ = Measure(warmup, static () => SandboxType.Scalar("I32"));
        _ = Measure(warmup, static () => CompiledRuntime.TypeScalar("I32"));
        _ = Measure(warmup, static () => CompiledRuntime.TypeScalar("MonsterId"));

        var allocatedScalar = Measure(iterations, static () => SandboxType.Scalar("I32"));
        var runtimeBuiltIn = Measure(iterations, static () => CompiledRuntime.TypeScalar("I32"));
        var runtimeOpaque = Measure(iterations, static () => CompiledRuntime.TypeScalar("MonsterId"));

        Console.WriteLine($"iterations = {iterations:N0}");
        Write("SandboxType.Scalar(\"I32\")", allocatedScalar);
        Write("CompiledRuntime.TypeScalar(\"I32\")", runtimeBuiltIn);
        Write("CompiledRuntime.TypeScalar(\"MonsterId\")", runtimeOpaque);
    }

    private static Measurement Measure(int iterations, Func<SandboxType> action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        SandboxType? last = null;
        for (var i = 0; i < iterations; i++)
        {
            last = action();
        }

        sw.Stop();
        GC.KeepAlive(last);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new Measurement(sw.Elapsed.TotalMilliseconds, allocated);
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine($"{name,-40} {measurement.Milliseconds,8:N1} ms {measurement.AllocatedBytes,14:N0} B");

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes);
}

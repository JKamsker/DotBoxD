using System.Diagnostics;

namespace DotBoxD.Kernels.Benchmarks.Measurements;

internal static class ScalingProbeMeter
{
    public static void Measure(string name, Action action, int iterations = 100_000)
    {
        for (var i = 0; i < Math.Min(iterations, 20_000); i++)
        {
            action();
        }

        var times = new double[5];
        var allocations = new double[5];
        for (var sample = 0; sample < times.Length; sample++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < iterations; i++)
            {
                action();
            }

            times[sample] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
            allocations[sample] = (double)(GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
        }

        Array.Sort(times);
        Console.WriteLine($"{name,-48} {times[2],10:F1} ns/op [{times[0]:F1}, {times[4]:F1}] {allocations[2],10:F1} B/op");
    }
}

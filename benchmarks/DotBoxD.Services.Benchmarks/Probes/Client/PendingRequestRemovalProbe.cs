using System.Diagnostics;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class PendingRequestRemovalProbe
{
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        WarmUp();
        var twoTraversal = MeasureTwoTraversalRemoval();
        var singleTraversal = MeasureSingleTraversalRemoval();

        Console.WriteLine("Pending-request matched removal probe");
        Console.WriteLine($"entries = {Iterations:N0}");
        Console.WriteLine("case                              total ms      ns/op    allocated B      B/op");
        Write("TryGetValue + Remove", twoTraversal);
        Write("Remove(key, out value)", singleTraversal);
    }

    private static Measurement MeasureTwoTraversalRemoval()
    {
        var entries = CreateEntries(Iterations);
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        long checksum = 0;
        for (var key = 0; key < Iterations; key++)
        {
            if (!entries.TryGetValue(key, out var value) || !entries.Remove(key))
            {
                throw new InvalidOperationException($"entry {key} was not removed");
            }

            checksum += value;
        }

        return CompleteMeasurement(entries, allocatedBefore, startedAt, checksum);
    }

    private static Measurement MeasureSingleTraversalRemoval()
    {
        var entries = CreateEntries(Iterations);
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        long checksum = 0;
        for (var key = 0; key < Iterations; key++)
        {
            if (!entries.Remove(key, out var value))
            {
                throw new InvalidOperationException($"entry {key} was not removed");
            }

            checksum += value;
        }

        return CompleteMeasurement(entries, allocatedBefore, startedAt, checksum);
    }

    private static Measurement CompleteMeasurement(
        Dictionary<int, int> entries,
        long allocatedBefore,
        long startedAt,
        long checksum)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var expectedChecksum = (long)Iterations * (Iterations - 1) / 2;
        if (entries.Count != 0 || checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"removal invariant failed: count={entries.Count}, checksum={checksum}");
        }

        return new Measurement(elapsed.TotalMilliseconds, allocated);
    }

    private static Dictionary<int, int> CreateEntries(int count)
    {
        var entries = new Dictionary<int, int>(count);
        for (var key = 0; key < count; key++)
        {
            entries.Add(key, key);
        }

        return entries;
    }

    private static void WarmUp()
    {
        var entries = CreateEntries(2);
        _ = entries.TryGetValue(0, out _);
        _ = entries.Remove(0);
        _ = entries.Remove(1, out _);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(string name, Measurement measurement) =>
        Console.WriteLine(
            $"{name,-32} {measurement.ElapsedMilliseconds,8:N1} " +
            $"{measurement.ElapsedMilliseconds * 1_000_000 / Iterations,11:N1} " +
            $"{measurement.AllocatedBytes,14:N0} " +
            $"{measurement.AllocatedBytes / (double)Iterations,10:N1}");

    private readonly record struct Measurement(double ElapsedMilliseconds, long AllocatedBytes);
}

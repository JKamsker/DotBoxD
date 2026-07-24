namespace DotBoxD.Kernels.Benchmarks.Plugins.AdapterValidation;

using System.Diagnostics;

internal static class EventAdapterValidationProbe
{
    public static void Run()
    {
        Console.WriteLine("Event-adapter validation-cache probe");
        Console.WriteLine("Predeclared targets: all warm cache lanes allocate <= 5% of baseline and median time <= 80%.");
        Console.WriteLine("Direct shape control: exact allocation parity and median time within +/- 5%.");
        Console.WriteLine("Cold controls: median time no worse than +5%; allocation <= baseline + 8 B/admission.");
        Console.WriteLine("case                                      iterations    total ms       ns/op       bytes       B/op       checksum");

        foreach (var scenario in EventAdapterValidationScenarios.Create())
        {
            scenario.Prepare();
            ForceGc();
            Write(scenario, Measure(scenario));
        }
    }

    private static Measurement Measure(EventAdapterValidationScenario scenario)
    {
        var checksum = 0L;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        for (var i = 0; i < scenario.Iterations; i++)
        {
            checksum += scenario.Invoke();
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (checksum != scenario.ExpectedChecksum)
        {
            throw new InvalidOperationException(
                $"Validation checksum failed for '{scenario.Name}': " +
                $"expected {scenario.ExpectedChecksum}, observed {checksum}.");
        }

        return new Measurement(elapsed, allocated, checksum);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(EventAdapterValidationScenario scenario, Measurement measurement)
        => Console.WriteLine(
            $"{scenario.Name,-40} {scenario.Iterations,10:N0} " +
            $"{measurement.Elapsed.TotalMilliseconds,11:N2} " +
            $"{measurement.Elapsed.TotalNanoseconds / scenario.Iterations,11:N2} " +
            $"{measurement.AllocatedBytes,11:N0} " +
            $"{measurement.AllocatedBytes / (double)scenario.Iterations,10:N2} " +
            $"{measurement.Checksum,14:N0}");

    private readonly record struct Measurement(
        TimeSpan Elapsed,
        long AllocatedBytes,
        long Checksum);
}

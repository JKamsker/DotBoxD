namespace DotBoxD.Kernels.Benchmarks.Plugins;

using System.Diagnostics;
using DotBoxD.Plugins.Runtime.Lifecycle;

internal static class LiveStateSyncRegistryProbe
{
    private const int WarmupIterations = 20_000;
    private const int MeasurementIterations = 1_000_000;
    private const double MeasurementNoiseBytesPerCall = 0.1;
    private static readonly Type[] StateTypes =
    [
        typeof(State1),
        typeof(State2),
        typeof(State3),
        typeof(State4),
        typeof(State5),
        typeof(State6),
        typeof(State7),
        typeof(State8)
    ];

    public static void Run()
    {
        var scenarios = new[]
        {
            Scenario.Create(
                "Sync x1", LiveUpdateMode.Sync, synchronizerCount: 1,
                historicalBytesPerCall: 32, expectedBytesPerCall: 0, expectedBytesSaved: 32),
            Scenario.Create(
                "Sync x8", LiveUpdateMode.Sync, synchronizerCount: 8,
                historicalBytesPerCall: 88, expectedBytesPerCall: 0, expectedBytesSaved: 88),
            Scenario.Create(
                "AsyncSet x1", LiveUpdateMode.AsyncSet, synchronizerCount: 1,
                historicalBytesPerCall: 120, expectedBytesPerCall: 88, expectedBytesSaved: 32),
            Scenario.Create(
                "AsyncSet x8", LiveUpdateMode.AsyncSet, synchronizerCount: 8,
                historicalBytesPerCall: 264, expectedBytesPerCall: 176, expectedBytesSaved: 88)
        };

        foreach (var scenario in scenarios)
        {
            _ = Measure(scenario, WarmupIterations);
        }

        ForceGc();
        Console.WriteLine($"live-state input synchronizations = {MeasurementIterations:N0}");
        Console.WriteLine("case               total ms       B/call    baseline       saved");
        foreach (var scenario in scenarios)
        {
            Write(scenario, Measure(scenario, MeasurementIterations));
        }
    }

    private static Measurement Measure(Scenario scenario, int iterations)
    {
        long checksum = 0;
        IReadOnlyList<Action>? deferredUpdates = null;
        var watch = Stopwatch.StartNew();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            deferredUpdates = scenario.Registry.SynchronizeForInput();
            checksum += deferredUpdates.Count;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        watch.Stop();
        var expectedChecksum = checked((long)scenario.DeferredUpdateCount * iterations);
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException($"expected checksum {expectedChecksum}, got {checksum}");
        }

        GC.KeepAlive(deferredUpdates);
        return new Measurement(watch.Elapsed, allocated);
    }

    private static void Write(Scenario scenario, Measurement measurement)
    {
        var bytesPerCall = measurement.AllocatedBytes / (double)MeasurementIterations;
        var bytesSaved = scenario.HistoricalBytesPerCall - bytesPerCall;
        if (bytesPerCall < scenario.ExpectedBytesPerCall ||
            bytesPerCall > scenario.ExpectedBytesPerCall + MeasurementNoiseBytesPerCall ||
            bytesSaved < scenario.ExpectedBytesSaved - MeasurementNoiseBytesPerCall ||
            bytesSaved > scenario.ExpectedBytesSaved + MeasurementNoiseBytesPerCall)
        {
            throw new InvalidOperationException(
                $"expected {scenario.Name} to allocate {scenario.ExpectedBytesPerCall:N1} B/call, " +
                $"got {bytesPerCall:N1} B/call");
        }

        Console.WriteLine(
            $"{scenario.Name,-16} {measurement.Elapsed.TotalMilliseconds,9:N1} " +
            $"{bytesPerCall,12:N1} {scenario.HistoricalBytesPerCall,11:N1} " +
            $"{scenario.HistoricalBytesPerCall - bytesPerCall,11:N1}");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record Scenario(
        string Name,
        LiveStateSyncRegistry Registry,
        int DeferredUpdateCount,
        double HistoricalBytesPerCall,
        double ExpectedBytesPerCall,
        double ExpectedBytesSaved)
    {
        public static Scenario Create(
            string name,
            LiveUpdateMode updateMode,
            int synchronizerCount,
            double historicalBytesPerCall,
            double expectedBytesPerCall,
            double expectedBytesSaved)
        {
            var registry = new LiveStateSyncRegistry(_ => updateMode);
            for (var i = 0; i < synchronizerCount; i++)
            {
                registry.Register(StateTypes[i], NoOp);
            }

            var deferredUpdateCount = updateMode == LiveUpdateMode.AsyncSet ? synchronizerCount : 0;
            return new Scenario(
                name,
                registry,
                deferredUpdateCount,
                historicalBytesPerCall,
                expectedBytesPerCall,
                expectedBytesSaved);
        }

        private static void NoOp()
        {
        }
    }

    private readonly record struct Measurement(TimeSpan Elapsed, long AllocatedBytes);

    private sealed class State1;
    private sealed class State2;
    private sealed class State3;
    private sealed class State4;
    private sealed class State5;
    private sealed class State6;
    private sealed class State7;
    private sealed class State8;
}

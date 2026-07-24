using System.Reflection;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class StreamReceivePopulationProbe
{
    private const int MeasurementRounds = 2_000;
    private const int WarmupRounds = 4;
    private static readonly TimeSpan DiagnosticGuard = TimeSpan.FromSeconds(5);

    private static readonly PropertyInfo? HasCacheProperty = GetConnectionProperty(
        "HasDedicatedReceiveCache");
    private static readonly PropertyInfo? SourceCountProperty = GetConnectionProperty(
        "DedicatedReceiveOperationCount");
    private static readonly PropertyInfo? AvailableCountProperty = GetConnectionProperty(
        "AvailableDedicatedReceiveOperationCount");

    public static async Task RunAsync(int sharedCapacity)
    {
        var atCapacity = await MeasureAsync(sharedCapacity, sharedCapacity).ConfigureAwait(false);
        var capacityPlusOne = await MeasureAsync(sharedCapacity + 1, sharedCapacity).ConfigureAwait(false);
        var sixtyFourPeers = await MeasureAsync(64, sharedCapacity).ConfigureAwait(false);
        var measurements = new[] { atCapacity, capacityPlusOne, sixtyFourPeers };

        Console.WriteLine();
        Console.WriteLine("Named-pipe Stream receive population one-time cost");
        Console.WriteLine(
            "peers overflow caches sources admission B activation B retained B  B/overflow");
        foreach (var measurement in measurements)
        {
            WriteOneTime(measurement, sharedCapacity);
        }

        Console.WriteLine();
        Console.WriteLine("Named-pipe Stream receive population steady-state start cost");
        Console.WriteLine(
            "peers       frames    ns/frame caller B/frame total B/frame allocated B");
        foreach (var measurement in measurements)
        {
            WriteSteady(measurement);
        }

        Console.WriteLine(
            $"invariants: {MeasurementRounds:N0} measured rounds after priming, admission, " +
            $"two-lane activation, and {WarmupRounds:N0} warm rounds; all receives suspended " +
            "on real named pipes before their peer wrote; retention deltas are forced-GC diagnostics");
    }

    private static async Task<Measurement> MeasureAsync(int peerCount, int sharedCapacity)
    {
        await using var harness = await StreamReceivePopulationHarness
            .CreateAsync(peerCount, sharedCapacity)
            .ConfigureAwait(false);
        harness.PrimePipes();
        harness.ClearScratch();
        ForceGc();
        var liveBefore = GC.GetTotalMemory(forceFullCollection: false);

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        harness.RunRound();
        var admissionBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var admissionDiagnostics = ReadDiagnostics(harness);
        ValidateDiagnostics(peerCount, sharedCapacity, admissionDiagnostics, expectActivated: false);

        allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        harness.ActivateDedicatedLanes();
        var activationBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        harness.RunRounds(WarmupRounds);
        harness.ClearScratch();
        var activatedDiagnostics = WaitForActivatedDiagnostics(
            peerCount,
            sharedCapacity,
            harness);

        ForceGc();
        var liveAfter = GC.GetTotalMemory(forceFullCollection: false);
        harness.KeepAlive();
        var steady = harness.MeasureStartCost(MeasurementRounds);
        _ = WaitForActivatedDiagnostics(peerCount, sharedCapacity, harness);

        return new Measurement(
            peerCount,
            admissionBytes,
            activationBytes,
            liveAfter - liveBefore,
            activatedDiagnostics,
            steady);
    }

    private static DedicatedDiagnostics WaitForActivatedDiagnostics(
        int peerCount,
        int sharedCapacity,
        StreamReceivePopulationHarness harness)
    {
        var diagnostics = ReadDiagnostics(harness);
        if (!diagnostics.IsAvailable)
        {
            return diagnostics;
        }

        var deadline = DateTime.UtcNow + DiagnosticGuard;
        while (!HasExpectedActivatedCounts(peerCount, sharedCapacity, diagnostics))
        {
            if (DateTime.UtcNow >= deadline)
            {
                ValidateDiagnostics(
                    peerCount,
                    sharedCapacity,
                    diagnostics,
                    expectActivated: true);
            }

            Thread.Yield();
            diagnostics = ReadDiagnostics(harness);
        }

        ValidateDiagnostics(peerCount, sharedCapacity, diagnostics, expectActivated: true);
        return diagnostics;
    }

    private static bool HasExpectedActivatedCounts(
        int peerCount,
        int sharedCapacity,
        DedicatedDiagnostics diagnostics)
    {
        var overflowCount = Math.Max(0, peerCount - sharedCapacity);
        return diagnostics.CacheCount == overflowCount &&
               diagnostics.SourceCount == overflowCount * 2 &&
               diagnostics.AvailableCount == overflowCount * 2;
    }

    private static void ValidateDiagnostics(
        int peerCount,
        int sharedCapacity,
        DedicatedDiagnostics diagnostics,
        bool expectActivated)
    {
        if (!diagnostics.IsAvailable)
        {
            return;
        }

        var overflowCount = Math.Max(0, peerCount - sharedCapacity);
        var expectedSources = expectActivated ? overflowCount * 2 : 0;
        if (diagnostics.CacheCount != overflowCount ||
            diagnostics.SourceCount != expectedSources ||
            diagnostics.AvailableCount != expectedSources)
        {
            throw new InvalidOperationException(
                $"Stream population diagnostics failed for {peerCount} peers: " +
                $"cache {diagnostics.CacheCount}/{overflowCount}, " +
                $"sources {diagnostics.SourceCount}/{expectedSources}, " +
                $"available {diagnostics.AvailableCount}/{expectedSources}.");
        }
    }

    private static DedicatedDiagnostics ReadDiagnostics(StreamReceivePopulationHarness harness)
    {
        if (HasCacheProperty is null ||
            SourceCountProperty is null ||
            AvailableCountProperty is null)
        {
            return default;
        }

        var cacheCount = 0;
        var sourceCount = 0;
        var availableCount = 0;
        for (var index = 0; index < harness.PeerCount; index++)
        {
            var connection = harness.GetConnection(index);
            if (HasCacheProperty.GetValue(connection) is true)
            {
                cacheCount++;
            }

            sourceCount += (int)(SourceCountProperty.GetValue(connection) ?? 0);
            availableCount += (int)(AvailableCountProperty.GetValue(connection) ?? 0);
        }

        return new DedicatedDiagnostics(true, cacheCount, sourceCount, availableCount);
    }

    private static PropertyInfo? GetConnectionProperty(string name) =>
        typeof(StreamConnection).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic);

    private static void WriteOneTime(Measurement measurement, int sharedCapacity)
    {
        var overflowCount = Math.Max(0, measurement.PeerCount - sharedCapacity);
        var retainedPerOverflow = overflowCount == 0
            ? "n/a"
            : (measurement.RetainedBytes / (double)overflowCount).ToString("N1");
        Console.WriteLine(
            $"{measurement.PeerCount,5:N0} {overflowCount,8:N0} " +
            $"{measurement.Diagnostics.FormattedCacheCount,6} " +
            $"{measurement.Diagnostics.FormattedSourceCount,7} " +
            $"{measurement.AdmissionBytes,11:N0} {measurement.ActivationBytes,12:N0} " +
            $"{measurement.RetainedBytes,10:N0} {retainedPerOverflow,11}");
    }

    private static void WriteSteady(Measurement measurement) =>
        Console.WriteLine(
            $"{measurement.PeerCount,5:N0} {measurement.Steady.FrameCount,12:N0} " +
            $"{measurement.Steady.NanosecondsPerFrame,11:N1} " +
            $"{measurement.Steady.CallerBytesPerFrame,14:N2} " +
            $"{measurement.Steady.TotalBytesPerFrame,13:N2} " +
            $"{measurement.Steady.TotalAllocatedBytes,11:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        int PeerCount,
        long AdmissionBytes,
        long ActivationBytes,
        long RetainedBytes,
        DedicatedDiagnostics Diagnostics,
        StreamReceivePopulationRun Steady);

    private readonly record struct DedicatedDiagnostics(
        bool IsAvailable,
        int CacheCount,
        int SourceCount,
        int AvailableCount)
    {
        public string FormattedCacheCount => IsAvailable ? CacheCount.ToString() : "n/a";

        public string FormattedSourceCount => IsAvailable ? SourceCount.ToString() : "n/a";
    }
}

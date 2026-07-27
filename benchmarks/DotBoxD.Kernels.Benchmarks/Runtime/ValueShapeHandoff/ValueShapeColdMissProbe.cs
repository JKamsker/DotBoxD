using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using static DotBoxD.Kernels.Benchmarks.Runtime.ValueShapeHandoff.ValueShapeHandoffProbeSupport;

namespace DotBoxD.Kernels.Benchmarks.Runtime.ValueShapeHandoff;

/// <summary>Measures ordinary cache misses while every cross-thread publication slot is occupied.</summary>
internal static class ValueShapeColdMissProbe
{
    private const int WarmupIterations = 500;
    private const int Iterations = 10_000;

    public static ColdMissMeasurements Measure()
    {
        ColdMissMeasurements measurements;
        using (ValueShapePublisherPopulation.Start())
        {
            WarmUp();
            measurements = new ColdMissMeasurements(
                Measure(CreateLists()),
                Measure(CreateMaps()),
                Measure(CreateRecords()));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return measurements;
    }

    private static void WarmUp()
    {
        _ = Measure(CreateLists(WarmupIterations));
        _ = Measure(CreateMaps(WarmupIterations));
        _ = Measure(CreateRecords(WarmupIterations));
    }

    private static Measurement Measure(SandboxValue[] values)
    {
        long elapsedTicks = 0;
        long allocatedBytes = 0;
        long checksum = 0;
        foreach (var value in values)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            var info = ValueShapeCache.GetOrMeasure(value);
            elapsedTicks += System.Diagnostics.Stopwatch.GetTimestamp() - startedAt;
            allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            checksum += info.Nodes + info.Shape.Elements + info.Shape.Depth;
        }

        var expectedPerValue = values[0] is MapValue ? 6L : 5L;
        var expectedChecksum = expectedPerValue * values.Length;
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"Cold-miss shape changed: checksum={checksum}/{expectedChecksum}.");
        }

        return new Measurement(values.Length, elapsedTicks, allocatedBytes, checksum);
    }

    private static SandboxValue[] CreateLists(int count = Iterations)
    {
        var values = new SandboxValue[count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = SandboxValue.FromList(
                [SandboxValue.FromInt32(index)],
                SandboxType.I32);
        }

        return values;
    }

    private static SandboxValue[] CreateMaps(int count = Iterations)
    {
        var values = new SandboxValue[count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>
                {
                    [SandboxValue.FromInt32(index)] = SandboxValue.FromInt32(index)
                },
                SandboxType.I32,
                SandboxType.I32);
        }

        return values;
    }

    private static SandboxValue[] CreateRecords(int count = Iterations)
    {
        var values = new SandboxValue[count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = SandboxValue.FromRecord([SandboxValue.FromInt32(index)]);
        }

        return values;
    }

    public readonly record struct ColdMissMeasurements(
        Measurement List,
        Measurement Map,
        Measurement Record);
}

using System.Diagnostics;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class KernelRpcClientResponseMeasurement
{
    private const int MeasurementRounds = 4;

    public static MeasurementPair MeasureAlternating(
        byte[] payload,
        Func<byte[], int> legacy,
        Func<byte[], int> direct,
        int warmupIterations,
        int measurementIterations)
    {
        _ = Measure(payload, legacy, warmupIterations);
        _ = Measure(payload, direct, warmupIterations);
        var legacyMeasurements = new Measurement[MeasurementRounds];
        var directMeasurements = new Measurement[MeasurementRounds];
        for (var round = 0; round < MeasurementRounds; round++)
        {
            if (round is 0 or 3)
            {
                legacyMeasurements[round] = Measure(payload, legacy, measurementIterations);
                directMeasurements[round] = Measure(payload, direct, measurementIterations);
            }
            else
            {
                directMeasurements[round] = Measure(payload, direct, measurementIterations);
                legacyMeasurements[round] = Measure(payload, legacy, measurementIterations);
            }
        }

        return new MeasurementPair(Median(legacyMeasurements), Median(directMeasurements));
    }

    private static Measurement Measure(byte[] payload, Func<byte[], int> decode, int iterations)
    {
        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            checksum += decode(payload);
        }

        var ended = Stopwatch.GetTimestamp();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new Measurement(
            Stopwatch.GetElapsedTime(started, ended).TotalMilliseconds,
            allocatedBytes,
            checksum,
            iterations);
    }

    private static Measurement Median(Measurement[] measurements)
    {
        var first = measurements[0];
        var sum = 0D;
        var minimum = double.MaxValue;
        var maximum = double.MinValue;
        foreach (var measurement in measurements)
        {
            if (measurement.AllocatedBytes != first.AllocatedBytes ||
                measurement.Checksum != first.Checksum)
            {
                throw new InvalidOperationException(
                    "Response projection measurements were not deterministic.");
            }

            sum += measurement.Milliseconds;
            minimum = Math.Min(minimum, measurement.Milliseconds);
            maximum = Math.Max(maximum, measurement.Milliseconds);
        }

        return first with { Milliseconds = (sum - minimum - maximum) / 2D };
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    internal readonly record struct MeasurementPair(Measurement Legacy, Measurement Direct);

    internal readonly record struct Measurement(
        double Milliseconds,
        long AllocatedBytes,
        long Checksum,
        int Iterations)
    {
        public double BytesPerOperation => (double)AllocatedBytes / Iterations;
    }
}

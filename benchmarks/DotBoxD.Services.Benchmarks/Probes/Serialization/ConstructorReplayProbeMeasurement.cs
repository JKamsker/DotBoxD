using System.Buffers;
using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;
using MessagePack;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class ConstructorReplayProbeMeasurement
{
    public const int WarmupIterations = 100_000;
    public const int MeasurementIterations = 1_000_000;

    public static byte[] GetExpectedWire<T>(MessagePackRpcSerializer serializer, T value)
        => MessagePackSerializer.Serialize(value, serializer.Options);

    public static void AssertSameWire(string name, ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"{name} changed the declared-type MessagePack wire payload.");
        }
    }

    public static Measurement Measure(
        string name,
        Action<ArrayBufferWriter<byte>> serialize,
        ReadOnlyMemory<byte> expectedWire)
    {
        var writer = new ArrayBufferWriter<byte>();
        RunWarmup(serialize, writer, expectedWire.Span);

        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < MeasurementIterations; i++)
        {
            serialize(writer);
            checksum += writer.WrittenCount;
            if (i + 1 < MeasurementIterations)
            {
                writer.Clear();
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        AssertMeasurement(name, writer.WrittenSpan, expectedWire.Span, checksum, allocated);

        return new Measurement(name, elapsed.TotalMilliseconds, allocated, checksum);
    }

    public static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-36} {measurement.Milliseconds,8:N1} " +
            $"{measurement.NanosecondsPerOperation,10:N1} " +
            $"{measurement.AllocatedBytes,16:N0} " +
            $"{measurement.BytesPerOperation,9:N1} {measurement.Checksum,10:N0}");

    public static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void RunWarmup(
        Action<ArrayBufferWriter<byte>> serialize,
        ArrayBufferWriter<byte> writer,
        ReadOnlySpan<byte> expectedWire)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            serialize(writer);
            if (i + 1 < WarmupIterations)
            {
                writer.Clear();
            }
        }

        AssertSameWire("warmup", expectedWire, writer.WrittenSpan);
        writer.Clear();
    }

    private static void AssertMeasurement(
        string name,
        ReadOnlySpan<byte> actualWire,
        ReadOnlySpan<byte> expectedWire,
        long checksum,
        long allocated)
    {
        AssertSameWire(name, expectedWire, actualWire);
        var expectedChecksum = checked((long)expectedWire.Length * MeasurementIterations);
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"{name} produced checksum {checksum:N0}; expected {expectedChecksum:N0}.");
        }

        if (allocated < 0 || allocated % MeasurementIterations != 0)
        {
            throw new InvalidOperationException(
                $"{name} produced a non-integral steady-state allocation total {allocated:N0}.");
        }
    }

    internal readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes,
        long Checksum)
    {
        public double NanosecondsPerOperation =>
            Milliseconds * 1_000_000 / MeasurementIterations;

        public double BytesPerOperation =>
            AllocatedBytes / (double)MeasurementIterations;
    }
}

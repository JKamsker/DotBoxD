using System.Buffers;
using System.Diagnostics;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class RpcRequestNameSerializationProbe
{
    private const int WarmupIterations = 10_000;
    private const int MeasurementIterations = 1_000_000;

    public static void RunLongNameScenarios()
    {
        Console.WriteLine("outbound request-name UTF-8 scenarios");
        Write(Measure("64 two-byte chars", new string('\u00e9', 64), new string('\u00f8', 64)));
        Write(Measure("128 two-byte chars", new string('\u00e9', 128), new string('\u00f8', 128)));
        Write(Measure("64 astral chars", Repeat("\U0001f600", 64), Repeat("\U0001f680", 64)));
        Write(Measure("257 ASCII uncached", new string('S', 257), new string('M', 257)));
        Write(Measure("129 two-byte uncached", new string('\u00e9', 129), new string('\u00f8', 129)));
    }

    private static Measurement Measure(string name, string serviceName, string methodName)
    {
        var serializer = new MessagePackRpcSerializer();
        var request = new RpcRequest
        {
            MessageId = 42,
            ServiceName = serviceName,
            MethodName = methodName,
        };
        var buffer = new ArrayBufferWriter<byte>();
        for (var i = 0; i < WarmupIterations; i++)
        {
            serializer.Serialize(buffer, request);
            buffer.Clear();
        }

        serializer.Serialize(buffer, request);
        var bytesPerRequest = buffer.WrittenCount;
        buffer.Clear();
        ForceGc();

        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < MeasurementIterations; i++)
        {
            serializer.Serialize(buffer, request);
            checksum += buffer.WrittenCount;
            buffer.Clear();
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var expectedChecksum = (long)bytesPerRequest * MeasurementIterations;
        if (checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"{name} produced checksum {checksum:N0}; expected {expectedChecksum:N0}.");
        }

        return new Measurement(name, elapsed, allocated, checksum);
    }

    private static string Repeat(string value, int count) => string.Concat(Enumerable.Repeat(value, count));

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-24} {measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.BytesPerOperation,8:N1} B/op checksum={measurement.Checksum:N0}");

    private readonly record struct Measurement(
        string Name,
        TimeSpan Elapsed,
        long AllocatedBytes,
        long Checksum)
    {
        public double NanosecondsPerOperation => Elapsed.TotalNanoseconds / MeasurementIterations;

        public double BytesPerOperation => AllocatedBytes / (double)MeasurementIterations;
    }
}

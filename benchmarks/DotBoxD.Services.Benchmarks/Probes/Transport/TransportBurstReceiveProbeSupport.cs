using System.Buffers.Binary;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class TransportBurstReceiveProbeSupport
{
    public static byte[] CreateBatch(int frameLength, int batchSize)
    {
        var frame = new byte[frameLength];
        BinaryPrimitives.WriteInt32LittleEndian(frame, frameLength);
        frame[8] = (byte)MessageType.Response;
        for (var index = MessageFramer.HeaderSize; index < frame.Length; index++)
        {
            frame[index] = unchecked((byte)(index * 31));
        }

        var batch = new byte[frameLength * batchSize];
        for (var index = 0; index < batchSize; index++)
        {
            frame.CopyTo(batch, index * frameLength);
        }

        return batch;
    }

    public static void Validate(
        ExchangeResult result,
        long? readCount,
        bool startReceiveBeforeWrite,
        int measurementBatches,
        int batchSize,
        int frameLength)
    {
        var frameCount = measurementBatches * batchSize;
        var expectedChecksum = (frameLength + unchecked((byte)((frameLength - 1) * 31))) *
            (long)frameCount;
        var expectedPendingReceives = startReceiveBeforeWrite ? measurementBatches : 0;
        if (result.Checksum != expectedChecksum ||
            readCount is <= 0 ||
            result.PendingReceives < expectedPendingReceives)
        {
            throw new InvalidOperationException(
                $"Probe invariants failed: checksum {result.Checksum:N0}/{expectedChecksum:N0}, " +
                $"reads {readCount?.ToString("N0") ?? "unavailable"}, pending receives " +
                $"{result.PendingReceives:N0}/{expectedPendingReceives:N0} minimum.");
        }
    }

    public static void Write(Measurement measurement, int measurementBatches, int batchSize)
    {
        var frameCount = measurementBatches * batchSize;
        var readsPerFrame = measurement.ReadCount / (double?)frameCount;
        Console.WriteLine(
            $"{measurement.Name,-25} {measurement.ElapsedMilliseconds,12:N1} " +
            $"{measurement.ElapsedMilliseconds * 1_000_000 / frameCount,14:N1} " +
            $"{measurement.AllocatedBytes,14:N0} {measurement.AllocatedBytes / (double)frameCount,10:N1} " +
            $"{readsPerFrame,12:N4} {measurement.PendingReceives / (double)frameCount,14:N4}");
    }

    public static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public readonly record struct ExchangeResult(long Checksum, int PendingReceives);

    public readonly record struct Measurement(
        string Name,
        double ElapsedMilliseconds,
        long AllocatedBytes,
        long? ReadCount,
        int PendingReceives);
}

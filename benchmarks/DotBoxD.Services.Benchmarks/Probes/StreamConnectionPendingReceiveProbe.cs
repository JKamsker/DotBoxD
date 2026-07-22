using System.Buffers.Binary;
using System.Diagnostics;
using DotBoxD.Services.Benchmarks.Support;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class StreamConnectionPendingReceiveProbe
{
    private const int FrameLength = 1_024;
    private const int WarmupIterations = 10_000;
    private const int MeasurementIterations = 100_000;

    public static async Task RunAsync()
    {
        var finite = await MeasureAsync("finite timeout", frameReadIdleTimeout: null)
            .ConfigureAwait(false);
        var infinite = await MeasureAsync("infinite timeout", Timeout.InfiniteTimeSpan)
            .ConfigureAwait(false);

        Console.WriteLine("StreamConnection forced-pending receive probe");
        Console.WriteLine("case                    total ms       ns/frame    allocated B    B/frame  reads/frame");
        Write(finite);
        Write(infinite);
        Console.WriteLine(
            $"invariants: {MeasurementIterations:N0} frames/lane, 2 pending reads/frame, " +
            $"{FrameLength:N0} bytes/frame, checksum {finite.Checksum:N0}/lane");
    }

    private static async Task<Measurement> MeasureAsync(
        string name,
        TimeSpan? frameReadIdleTimeout)
    {
        var frameBytes = CreateFrame();
        using var stream = new ForcedPendingFrameStream(frameBytes);
        await using var connection = new StreamConnection(
            stream,
            ownsStream: false,
            frameReadIdleTimeout: frameReadIdleTimeout);

        await ReadFramesAsync(connection, WarmupIterations).ConfigureAwait(false);

        ForceGc();
        var readsBefore = stream.ReadCount;
        var pendingReadsBefore = stream.PendingReadCount;
        var completedReadsBefore = stream.CompletedReadCount;
        var bytesBefore = stream.BytesRead;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var startedAt = Stopwatch.GetTimestamp();

        var checksum = await ReadFramesAsync(connection, MeasurementIterations).ConfigureAwait(false);

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var readCount = stream.ReadCount - readsBefore;
        var pendingReadCount = stream.PendingReadCount - pendingReadsBefore;
        var completedReadCount = stream.CompletedReadCount - completedReadsBefore;
        var bytesRead = stream.BytesRead - bytesBefore;
        Validate(checksum, readCount, pendingReadCount, completedReadCount, bytesRead, frameBytes);

        // The final stream completion runs continuations inline on its dedicated producer thread.
        // Move disposal away from that thread before joining it.
        await Task.Yield();
        return new Measurement(name, elapsed.TotalMilliseconds, allocated, checksum, readCount);
    }

    private static async Task<long> ReadFramesAsync(StreamConnection connection, int iterations)
    {
        long checksum = 0;
        for (var i = 0; i < iterations; i++)
        {
            var frame = await connection.ReceiveFrameValueAsync().ConfigureAwait(false);
            try
            {
                var bytes = frame.Memory.Span;
                checksum += BinaryPrimitives.ReadInt32LittleEndian(bytes);
                checksum += bytes[4] + bytes[FrameLength / 2] + bytes[^1];
            }
            finally
            {
                frame.Dispose();
            }
        }

        return checksum;
    }

    private static byte[] CreateFrame()
    {
        var frame = new byte[FrameLength];
        BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length);
        for (var i = sizeof(int); i < frame.Length; i++)
        {
            frame[i] = unchecked((byte)(i * 31));
        }

        return frame;
    }

    private static void Validate(
        long checksum,
        long readCount,
        long pendingReadCount,
        long completedReadCount,
        long bytesRead,
        byte[] frame)
    {
        var checksumPerFrame =
            frame.Length + frame[4] + frame[FrameLength / 2] + frame[^1];
        var expectedChecksum = checksumPerFrame * (long)MeasurementIterations;
        var expectedReadCount = MeasurementIterations * 2L;
        var expectedByteCount = MeasurementIterations * (long)FrameLength;
        if (checksum != expectedChecksum ||
            readCount != expectedReadCount ||
            pendingReadCount != expectedReadCount ||
            completedReadCount != expectedReadCount ||
            bytesRead != expectedByteCount)
        {
            throw new InvalidOperationException(
                $"Probe invariants failed: checksum {checksum:N0}/{expectedChecksum:N0}, " +
                $"reads {readCount:N0}/{expectedReadCount:N0}, pending {pendingReadCount:N0}/" +
                $"{expectedReadCount:N0}, completed {completedReadCount:N0}/{expectedReadCount:N0}, " +
                $"bytes {bytesRead:N0}/{expectedByteCount:N0}.");
        }
    }

    private static void Write(Measurement measurement)
    {
        Console.WriteLine(
            $"{measurement.Name,-22} {measurement.ElapsedMilliseconds,8:N1} " +
            $"{measurement.NanosecondsPerFrame,14:N1} {measurement.AllocatedBytes,14:N0} " +
            $"{measurement.BytesPerFrame,10:N1} {measurement.ReadsPerFrame,12:N1}");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        string Name,
        double ElapsedMilliseconds,
        long AllocatedBytes,
        long Checksum,
        long ReadCount)
    {
        public double NanosecondsPerFrame =>
            ElapsedMilliseconds * 1_000_000 / MeasurementIterations;

        public double BytesPerFrame => AllocatedBytes / (double)MeasurementIterations;

        public double ReadsPerFrame => ReadCount / (double)MeasurementIterations;
    }
}

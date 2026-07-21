using System.Buffers.Binary;
using System.Diagnostics;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class StreamConnectionReceiveTrackingProbe
{
    private const int FrameLength = 1_024;
    private const int WarmupIterations = 10_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        var legacy = Measure(Iterations, simulateLegacyTracking: true);
        var current = Measure(Iterations, simulateLegacyTracking: false);
        var finiteFrame = MeasureFrames(Iterations, frameReadIdleTimeout: null);
        var infiniteFrame = MeasureFrames(Iterations, Timeout.InfiniteTimeSpan);

        Console.WriteLine("StreamConnection receive tracking probe");
        Write("Owned receive with legacy tracking", legacy);
        Write("Owned receive current", current);
        Write("ValueTask frame finite timeout", finiteFrame);
        Write("ValueTask frame infinite timeout", infiniteFrame);
    }

    private static Measurement Measure(int iterations, bool simulateLegacyTracking)
    {
        using var stream = new MemoryStream(Array.Empty<byte>());
        var connection = new StreamConnection(stream, ownsStream: true);
        var activeReceives = 0;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            if (simulateLegacyTracking)
            {
                Interlocked.Increment(ref activeReceives);
            }

            try
            {
                var payload = connection.ReceiveAsync().GetAwaiter().GetResult();
                if (!ReferenceEquals(payload, Payload.Empty))
                {
                    payload.Dispose();
                }
            }
            finally
            {
                if (simulateLegacyTracking)
                {
                    Interlocked.Decrement(ref activeReceives);
                }
            }
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return new Measurement(
            iterations,
            elapsed.TotalMilliseconds,
            allocated);
    }

    private static Measurement MeasureFrames(int iterations, TimeSpan? frameReadIdleTimeout)
    {
        var frame = new byte[FrameLength];
        BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length);
        using var stream = new MemoryStream(frame, writable: false);
        var connection = new StreamConnection(
            stream,
            ownsStream: false,
            frameReadIdleTimeout: frameReadIdleTimeout);

        for (var i = 0; i < WarmupIterations; i++)
        {
            ReadFrame(connection, stream);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        long checksum = 0;
        for (var i = 0; i < iterations; i++)
        {
            checksum += ReadFrame(connection, stream);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (checksum != (long)FrameLength * iterations)
        {
            throw new InvalidOperationException($"Expected checksum {(long)FrameLength * iterations}, observed {checksum}.");
        }

        return new Measurement(iterations, elapsed.TotalMilliseconds, allocated);
    }

    private static int ReadFrame(StreamConnection connection, MemoryStream stream)
    {
        stream.Position = 0;
        var pending = connection.ReceiveValueAsync();
        if (!pending.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Memory-backed receive did not complete synchronously.");
        }

        var payload = pending.Result;
        try
        {
            return payload.Length;
        }
        finally
        {
            payload.Dispose();
        }
    }

    private static void Write(string name, Measurement measurement)
    {
        Console.WriteLine(
            $"{name,-35} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,12:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op");
    }

    private readonly record struct Measurement(
        int Iterations,
        double Milliseconds,
        long AllocatedBytes)
    {
        public double NanosecondsPerOperation => Milliseconds * 1_000_000 / Iterations;

        public double BytesPerOperation => AllocatedBytes / (double)Iterations;
    }
}

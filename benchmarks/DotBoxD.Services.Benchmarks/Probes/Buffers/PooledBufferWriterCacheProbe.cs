using System.Buffers;
using System.Diagnostics;
using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class PooledBufferWriterCacheProbe
{
    private const int SameThreadIterations = 5_000_000;
    private const int CrossThreadIterations = 1_000_000;
    private const int LargeBufferIterations = 1_000_000;
    private const int CrossThreadLargeBufferIterations = 500_000;
    private const int PublicWriterIterations = 200_000;
    private const int SameThreadWarmup = 100_000;
    private const int CrossThreadWarmup = 20_000;
    private const int LargeBufferWarmup = 20_000;

    public static void Run()
    {
        PrewarmArrayPool();
        var firstInternalRent = MeasureFirstInternalRent();
        for (var i = 0; i < SameThreadWarmup; i++)
        {
            UseAndDispose(i);
        }

        for (var i = 0; i < CrossThreadWarmup; i++)
        {
            UsePublicWriter(i);
        }

        using var returner = new CrossThreadWriterReturner();
        for (var i = 0; i < CrossThreadWarmup; i++)
        {
            UseAndReturn(returner, i);
        }

        for (var i = 0; i < LargeBufferWarmup; i++)
        {
            UseAndDispose(i, 8192);
            UseAndReturn(returner, i, 8192);
        }

        Console.WriteLine("PooledBufferWriter cache probe");
        Console.WriteLine("case                                ms      ns/op    allocated B      B/op checksum");
        Write(firstInternalRent);
        Write(MeasurePublicWriters());
        Write(MeasureSameThread());
        Write(MeasureCrossThread(returner));
        Write(MeasureSameThreadLargeBuffer());
        Write(MeasureCrossThreadLargeBuffer(returner));
    }

    private static Measurement MeasureFirstInternalRent()
    {
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        var checksum = UseAndDispose(0);
        return Measurement.Create(
            "first internal rent, pool warm",
            1,
            started,
            allocatedBefore,
            checksum);
    }

    private static Measurement MeasurePublicWriters()
    {
        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < PublicWriterIterations; i++)
        {
            checksum += UsePublicWriter(i);
        }

        return Measurement.Create(
            "public writer construction",
            PublicWriterIterations,
            started,
            allocatedBefore,
            checksum);
    }

    private static Measurement MeasureSameThread()
    {
        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < SameThreadIterations; i++)
        {
            checksum += UseAndDispose(i);
        }

        return Measurement.Create(
            "same-thread rent + return",
            SameThreadIterations,
            started,
            allocatedBefore,
            checksum);
    }

    private static Measurement MeasureCrossThread(CrossThreadWriterReturner returner)
    {
        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < CrossThreadIterations; i++)
        {
            checksum += UseAndReturn(returner, i);
        }

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        return new Measurement(
            "cross-thread rent + return",
            CrossThreadIterations,
            elapsed,
            allocated,
            checksum);
    }

    private static Measurement MeasureSameThreadLargeBuffer()
    {
        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < LargeBufferIterations; i++)
        {
            checksum += UseAndDispose(i, 8192);
        }

        return Measurement.Create(
            "same-thread 8 KiB fallback",
            LargeBufferIterations,
            started,
            allocatedBefore,
            checksum);
    }

    private static Measurement MeasureCrossThreadLargeBuffer(CrossThreadWriterReturner returner)
    {
        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < CrossThreadLargeBufferIterations; i++)
        {
            checksum += UseAndReturn(returner, i, 8192);
        }

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        return new Measurement(
            "cross-thread 8 KiB fallback",
            CrossThreadLargeBufferIterations,
            elapsed,
            allocated,
            checksum);
    }

    private static int UseAndDispose(int value, int initialCapacity = 256)
    {
        using var writer = PooledBufferWriter.Rent(initialCapacity);
        WriteByte(writer, value);
        return writer.WrittenCount;
    }

    private static int UsePublicWriter(int value)
    {
        using var writer = new PooledBufferWriter();
        WriteByte(writer, value);
        return writer.WrittenCount;
    }

    private static int UseAndReturn(
        CrossThreadWriterReturner returner,
        int value,
        int initialCapacity = 256)
    {
        var writer = PooledBufferWriter.Rent(initialCapacity);
        WriteByte(writer, value);
        var written = writer.WrittenCount;
        returner.Return(writer);
        return written;
    }

    private static void WriteByte(PooledBufferWriter writer, int value)
    {
        writer.GetSpan(1)[0] = unchecked((byte)value);
        writer.Advance(1);
    }

    private static void Write(Measurement measurement) =>
        Console.WriteLine(
            $"{measurement.Name,-32} {measurement.Milliseconds,8:N1} " +
            $"{measurement.NanosecondsPerOperation,10:N1} " +
            $"{measurement.AllocatedBytes,14:N0} " +
            $"{measurement.BytesPerOperation,9:N1} {measurement.Checksum,8:N0}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void PrewarmArrayPool()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(256);
        ArrayPool<byte>.Shared.Return(buffer);
    }

    private readonly record struct Measurement(
        string Name,
        int Iterations,
        double Milliseconds,
        long AllocatedBytes,
        long Checksum)
    {
        public double NanosecondsPerOperation => Milliseconds * 1_000_000 / Iterations;

        public double BytesPerOperation => AllocatedBytes / (double)Iterations;

        public static Measurement Create(
            string name,
            int iterations,
            long started,
            long allocatedBefore,
            long checksum) =>
            new(
                name,
                iterations,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
                checksum);
    }

}

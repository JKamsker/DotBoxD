using System.Diagnostics;
using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class PooledBufferWriterCacheProbe
{
    private const int SameThreadIterations = 5_000_000;
    private const int CrossThreadIterations = 1_000_000;
    private const int SameThreadWarmup = 100_000;
    private const int CrossThreadWarmup = 20_000;

    public static void Run()
    {
        for (var i = 0; i < SameThreadWarmup; i++)
        {
            UseAndDispose(i);
        }

        using var returner = new CrossThreadReturner();
        for (var i = 0; i < CrossThreadWarmup; i++)
        {
            UseAndReturn(returner, i);
        }

        Console.WriteLine("PooledBufferWriter cache probe");
        Console.WriteLine("case                                ms      ns/op    allocated B      B/op checksum");
        Write(MeasureSameThread());
        Write(MeasureCrossThread(returner));
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

    private static Measurement MeasureCrossThread(CrossThreadReturner returner)
    {
        ForceGc();
        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < CrossThreadIterations; i++)
        {
            checksum += UseAndReturn(returner, i);
        }

        return Measurement.Create(
            "cross-thread rent + return",
            CrossThreadIterations,
            started,
            allocatedBefore,
            checksum);
    }

    private static int UseAndDispose(int value)
    {
        using var writer = PooledBufferWriter.Rent();
        WriteByte(writer, value);
        return writer.WrittenCount;
    }

    private static int UseAndReturn(CrossThreadReturner returner, int value)
    {
        var writer = PooledBufferWriter.Rent();
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

    private sealed class CrossThreadReturner : IDisposable
    {
        private readonly Thread _thread;
        private PooledBufferWriter? _writer;
        private Exception? _failure;
        private int _published;
        private int _completed;
        private int _nextSequence;
        private int _stopping;

        public CrossThreadReturner()
        {
            _thread = new Thread(ReturnLoop)
            {
                IsBackground = true,
                Name = "PooledBufferWriter benchmark returner",
            };
            _thread.Start();
        }

        public void Return(PooledBufferWriter writer)
        {
            var sequence = ++_nextSequence;
            _writer = writer;
            Volatile.Write(ref _published, sequence);

            while (Volatile.Read(ref _completed) != sequence)
            {
                Thread.SpinWait(1);
            }

            if (_failure is { } failure)
            {
                throw new InvalidOperationException("Cross-thread writer return failed.", failure);
            }
        }

        public void Dispose()
        {
            Volatile.Write(ref _stopping, 1);
            _thread.Join();
            if (_failure is { } failure)
            {
                throw new InvalidOperationException("Cross-thread writer return failed.", failure);
            }
        }

        private void ReturnLoop()
        {
            var sequence = 1;
            while (true)
            {
                while (Volatile.Read(ref _published) < sequence)
                {
                    if (Volatile.Read(ref _stopping) != 0)
                    {
                        return;
                    }

                    Thread.SpinWait(1);
                }

                var writer = _writer;
                _writer = null;
                try
                {
                    (writer ?? throw new InvalidOperationException("No writer was published.")).Dispose();
                }
                catch (Exception ex)
                {
                    _failure = ex;
                }
                finally
                {
                    Volatile.Write(ref _completed, sequence);
                }

                sequence++;
            }
        }
    }
}

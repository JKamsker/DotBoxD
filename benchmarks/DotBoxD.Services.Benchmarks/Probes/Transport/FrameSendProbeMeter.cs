using System.Diagnostics;

namespace DotBoxD.Services.Benchmarks.Probes;

internal sealed class FrameSendProbeMeter(
    int warmupIterations,
    int iterations,
    int frameLength,
    long frameChecksum)
{
    public FrameSendMeasurement Measure(
        string name,
        ControlledWriteStream stream,
        Action send)
    {
        for (var i = 0; i < warmupIterations; i++)
        {
            send();
        }

        var before = stream.Snapshot();
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            send();
        }

        var finished = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var elapsed = Stopwatch.GetElapsedTime(started, finished).TotalMilliseconds;
        VerifyOutput(name, before, stream.Snapshot());
        return new FrameSendMeasurement(name, elapsed, allocated, iterations);
    }

    public static void Write(FrameSendMeasurement measurement) =>
        Console.WriteLine(
            $"{measurement.Name,-40} {measurement.Milliseconds,9:N1} ms " +
            $"{measurement.NanosecondsPerOperation,9:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.BytesPerOperation,9:N1} B/op");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void VerifyOutput(string name, WriteSnapshot before, WriteSnapshot after)
    {
        var writes = after.Writes - before.Writes;
        var flushes = after.Flushes - before.Flushes;
        var bytes = after.Bytes - before.Bytes;
        var checksum = after.Checksum - before.Checksum;
        var expectedBytes = (long)iterations * frameLength;
        var expectedChecksum = iterations * frameChecksum;
        if (writes != iterations ||
            flushes != iterations ||
            bytes != expectedBytes ||
            checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"{name} output mismatch: writes={writes:N0}, flushes={flushes:N0}, " +
                $"bytes={bytes:N0}, checksum={checksum}; expected {iterations:N0}, " +
                $"{iterations:N0}, {expectedBytes:N0}, {expectedChecksum}.");
        }
    }
}

internal readonly record struct FrameSendMeasurement(
    string Name,
    double Milliseconds,
    long AllocatedBytes,
    int Iterations)
{
    public double NanosecondsPerOperation => Milliseconds * 1_000_000 / Iterations;

    public double BytesPerOperation => AllocatedBytes / (double)Iterations;
}

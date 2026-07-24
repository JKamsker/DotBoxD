using System.Diagnostics;

namespace DotBoxD.Services.Benchmarks.Probes;

internal enum PendingSendStage
{
    Gate,
    Write,
    Flush,
}

internal enum PendingSendKind
{
    Raw,
    Owned,
}

internal readonly record struct PendingSendCallSample(long ElapsedTicks, long CallerAllocatedBytes);

internal readonly record struct SendOutputSnapshot(
    long Writes,
    long Flushes,
    long Bytes,
    long Checksum);

internal sealed class PendingSendStageMeter(
    int warmupIterations,
    int iterations,
    int expectedFlushesPerSend)
{
    public int TotalOperations => warmupIterations + iterations;

    public PendingSendStageMeasurement Measure(
        string name,
        Func<PendingSendCallSample> send,
        Func<SendOutputSnapshot> snapshot)
    {
        for (var index = 0; index < warmupIterations; index++)
        {
            _ = send();
        }

        var before = snapshot();
        ForceGc();
        var totalAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long callTicks = 0;
        long callerAllocated = 0;
        for (var index = 0; index < iterations; index++)
        {
            var sample = send();
            callTicks += sample.ElapsedTicks;
            callerAllocated += sample.CallerAllocatedBytes;
        }

        var totalAllocated = GC.GetTotalAllocatedBytes(precise: true) - totalAllocatedBefore;
        VerifyOutput(name, before, snapshot());
        return new PendingSendStageMeasurement(
            name,
            iterations,
            callTicks,
            callerAllocated,
            totalAllocated);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void VerifyOutput(
        string name,
        SendOutputSnapshot before,
        SendOutputSnapshot after)
    {
        var writes = after.Writes - before.Writes;
        var flushes = after.Flushes - before.Flushes;
        var bytes = after.Bytes - before.Bytes;
        var checksum = after.Checksum - before.Checksum;
        var expectedBytes = (long)iterations * SendProbeFrame.Length;
        var expectedChecksum = iterations * SendProbeFrame.Checksum;
        var expectedFlushes = (long)iterations * expectedFlushesPerSend;
        if (writes != iterations ||
            flushes != expectedFlushes ||
            bytes != expectedBytes ||
            checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"{name} output mismatch: writes={writes:N0}, flushes={flushes:N0}, " +
                $"bytes={bytes:N0}, checksum={checksum}; expected {iterations:N0}, " +
                $"{expectedFlushes:N0}, {expectedBytes:N0}, {expectedChecksum}.");
        }
    }
}

internal readonly record struct PendingSendStageMeasurement(
    string Name,
    int Iterations,
    long CallTicks,
    long CallerAllocatedBytes,
    long LoopProcessAllocatedBytes)
{
    public double CallNanosecondsPerOperation =>
        CallTicks * (1_000_000_000d / Stopwatch.Frequency) / Iterations;

    public double CallerBytesPerOperation => CallerAllocatedBytes / (double)Iterations;

    public double LoopProcessBytesPerOperation => LoopProcessAllocatedBytes / (double)Iterations;
}

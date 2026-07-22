using System.Diagnostics;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class PendingOwnedFrameSendProbe
{
    private const int WarmupIterations = 20_000;
    private const int Iterations = 1_000_000;
    private static readonly byte[] s_rawFrame = CreateRawFrame();
    private static readonly long s_frameChecksum = CalculateChecksum(s_rawFrame);

    public static void Run()
    {
        using var synchronousStream = new ControlledWriteStream(forcePending: false);
        using var pendingStream = new ControlledWriteStream(forcePending: true);
        var synchronousConnection = new StreamConnection(synchronousStream, ownsStream: false);
        var pendingConnection = new StreamConnection(pendingStream, ownsStream: false);
        try
        {
            VerifyOwnedFrameLifetime(pendingConnection, pendingStream);
            var synchronousRaw = Measure(
                "synchronous raw send",
                synchronousStream,
                () => SendRaw(synchronousConnection, synchronousStream));
            var synchronousOwned = Measure(
                "synchronous owned-frame send",
                synchronousStream,
                () => SendOwnedFrame(synchronousConnection, synchronousStream));
            var pendingRaw = Measure(
                "forced-pending raw send",
                pendingStream,
                () => SendRaw(pendingConnection, pendingStream));
            var pendingOwned = Measure(
                "forced-pending owned-frame send",
                pendingStream,
                () => SendOwnedFrame(pendingConnection, pendingStream));

            Console.WriteLine("Pending owned-frame send probe");
            Console.WriteLine($"iterations = {Iterations:N0}; warmup = {WarmupIterations:N0}");
            Console.WriteLine(
                $"verified frame bytes = {s_rawFrame.Length}; " +
                $"per-frame checksum = {s_frameChecksum}");
            Write(synchronousRaw);
            Write(synchronousOwned);
            Write(pendingRaw);
            Write(pendingOwned);
            var synchronousOwnershipCost =
                synchronousOwned.NanosecondsPerOperation - synchronousRaw.NanosecondsPerOperation;
            var pendingOwnershipCost =
                pendingOwned.NanosecondsPerOperation - pendingRaw.NanosecondsPerOperation;
            Console.WriteLine(
                $"pending ownership overhead       " +
                $"{pendingOwnershipCost,9:N1} ns/op " +
                $"{pendingOwned.BytesPerOperation - pendingRaw.BytesPerOperation,9:N1} B/op");
            Console.WriteLine(
                $"pending state-machine overhead   " +
                $"{pendingOwnershipCost - synchronousOwnershipCost,9:N1} ns/op");
        }
        finally
        {
            synchronousConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            pendingConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static Measurement Measure(
        string name,
        ControlledWriteStream stream,
        Action send)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            send();
        }

        var before = stream.Snapshot();
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < Iterations; i++)
        {
            send();
        }

        var finished = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var elapsed = Stopwatch.GetElapsedTime(started, finished).TotalMilliseconds;
        VerifyOutput(name, before, stream.Snapshot());
        return new Measurement(name, elapsed, allocated);
    }

    private static void SendRaw(StreamConnection connection, ControlledWriteStream stream)
    {
        var pending = connection.SendValueAsync(s_rawFrame);
        CompleteSend(pending, stream);
    }

    private static void SendOwnedFrame(StreamConnection connection, ControlledWriteStream stream)
    {
        var frame = CreateOwnedFrame();
        var pending = connection.SendFrameValueAsync(frame);
        CompleteSend(pending, stream);
    }

    private static void CompleteSend(ValueTask pending, ControlledWriteStream stream)
    {
        if (!stream.ForcePending)
        {
            if (!pending.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("The synchronous write did not complete synchronously.");
            }

            pending.GetAwaiter().GetResult();
            return;
        }

        if (pending.IsCompleted)
        {
            throw new InvalidOperationException("The controlled write did not remain pending.");
        }

        stream.CompletePendingWrite();
        pending.GetAwaiter().GetResult();
        stream.ResetCompletedWrite();
    }

    private static void VerifyOwnedFrameLifetime(
        StreamConnection connection,
        ControlledWriteStream stream)
    {
        var frame = CreateOwnedFrame();
        var pending = connection.SendFrameValueAsync(frame);
        if (pending.IsCompleted)
        {
            throw new InvalidOperationException("The lifetime check did not remain pending.");
        }

        _ = frame.WrittenMemory;
        stream.CompletePendingWrite();
        pending.GetAwaiter().GetResult();
        stream.ResetCompletedWrite();
        try
        {
            _ = frame.WrittenMemory;
            throw new InvalidOperationException("The completed send did not release its frame.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static PooledBufferWriter CreateOwnedFrame()
    {
        var frame = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        return frame;
    }

    private static byte[] CreateRawFrame()
    {
        using var frame = CreateOwnedFrame();
        return frame.WrittenMemory.ToArray();
    }

    private static long CalculateChecksum(ReadOnlySpan<byte> frame)
    {
        var checksum = 0L;
        for (var index = 0; index < frame.Length; index++)
        {
            checksum += (index + 1L) * frame[index];
        }

        return checksum;
    }

    private static void VerifyOutput(string name, WriteSnapshot before, WriteSnapshot after)
    {
        var writes = after.Writes - before.Writes;
        var flushes = after.Flushes - before.Flushes;
        var bytes = after.Bytes - before.Bytes;
        var checksum = after.Checksum - before.Checksum;
        var expectedBytes = (long)Iterations * s_rawFrame.Length;
        var expectedChecksum = Iterations * s_frameChecksum;
        if (writes != Iterations ||
            flushes != Iterations ||
            bytes != expectedBytes ||
            checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"{name} output mismatch: writes={writes:N0}, flushes={flushes:N0}, " +
                $"bytes={bytes:N0}, checksum={checksum}; expected {Iterations:N0}, " +
                $"{Iterations:N0}, {expectedBytes:N0}, {expectedChecksum}.");
        }
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(Measurement measurement) =>
        Console.WriteLine(
            $"{measurement.Name,-34} {measurement.Milliseconds,9:N1} ms " +
            $"{measurement.NanosecondsPerOperation,9:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.BytesPerOperation,9:N1} B/op");

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes)
    {
        public double NanosecondsPerOperation => Milliseconds * 1_000_000 / Iterations;

        public double BytesPerOperation => AllocatedBytes / (double)Iterations;
    }
}

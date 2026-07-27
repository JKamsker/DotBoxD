using System.Buffers.Binary;
using System.Diagnostics;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class StreamConnectionSendGateProbe
{
    private const int WarmupIterations = 10_000;
    private const int Iterations = 1_000_000;
    private static readonly byte[] s_frame = CreateFrame();

    public static void Run()
    {
        using var stream = new SynchronousSinkStream();
        var connection = new StreamConnection(stream, ownsStream: false);
        using var liveTokenSource = new CancellationTokenSource();

        try
        {
            var defaultToken = Measure(connection, stream, CancellationToken.None);
            var liveToken = Measure(connection, stream, liveTokenSource.Token);

            Console.WriteLine("StreamConnection uncontended send-gate probe");
            Write("Default cancellation token", defaultToken);
            Write("Live reusable cancellation token", liveToken);
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static Measurement Measure(
        StreamConnection connection,
        SynchronousSinkStream stream,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            Send(connection, cancellationToken);
        }

        var writesBefore = stream.WriteCount;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < Iterations; i++)
        {
            Send(connection, cancellationToken);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var writes = stream.WriteCount - writesBefore;
        if (writes != Iterations)
        {
            throw new InvalidOperationException($"Expected {Iterations:N0} writes, observed {writes:N0}.");
        }

        return new Measurement(Iterations, elapsed.TotalMilliseconds, allocated);
    }

    private static void Send(StreamConnection connection, CancellationToken cancellationToken)
    {
        var pending = connection.SendValueAsync(s_frame, cancellationToken);
        if (!pending.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("The synchronous sink send did not complete synchronously.");
        }

        pending.GetAwaiter().GetResult();
    }

    private static byte[] CreateFrame()
    {
        var frame = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), 1);
        frame[8] = (byte)MessageType.Request;
        return frame;
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

    private sealed class SynchronousSinkStream : Stream
    {
        public int WriteCount { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            return ValueTask.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

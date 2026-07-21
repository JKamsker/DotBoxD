using System.Diagnostics;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class RpcPeerFrameSendProbe
{
    private const int WarmupIterations = 10_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        using var stream = new SynchronousSinkStream();
        var connection = new StreamConnection(stream, ownsStream: false);
        using var sender = new RpcPeerSender(connection, static () => false);

        try
        {
            var direct = Measure(
                "Direct frame channel",
                stream,
                connection.SendFrameValueAsync);
            var peer = Measure(
                "RpcPeerSender frame path",
                stream,
                sender.SendFrameValueAsync);

            Console.WriteLine("RPC peer frame-send overhead probe");
            Console.WriteLine($"iterations = {Iterations:N0}; warmup = {WarmupIterations:N0}");
            Write(direct);
            Write(peer);
            Console.WriteLine(
                $"{"Peer overhead",-28} " +
                $"{peer.NanosecondsPerOperation - direct.NanosecondsPerOperation,8:N1} ns/op " +
                $"{peer.BytesPerOperation - direct.BytesPerOperation,8:N1} B/op");
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static Measurement Measure(
        string name,
        SynchronousSinkStream stream,
        Func<PooledBufferWriter, CancellationToken, ValueTask> send)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            SendOnce(send);
        }

        var writesBefore = stream.WriteCount;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < Iterations; i++)
        {
            SendOnce(send);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var writes = stream.WriteCount - writesBefore;
        if (writes != Iterations)
        {
            throw new InvalidOperationException($"{name} wrote {writes:N0} frames; expected {Iterations:N0}.");
        }

        return new Measurement(name, elapsed.TotalMilliseconds, allocated);
    }

    private static void SendOnce(Func<PooledBufferWriter, CancellationToken, ValueTask> send)
    {
        var frame = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        var pending = send(frame, CancellationToken.None);
        if (!pending.IsCompletedSuccessfully)
        {
            pending.AsTask().GetAwaiter().GetResult();
            throw new InvalidOperationException("The synchronous sink send did not complete synchronously.");
        }

        pending.GetAwaiter().GetResult();
    }

    private static void Write(Measurement measurement) =>
        Console.WriteLine(
            $"{measurement.Name,-28} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,12:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op");

    private readonly record struct Measurement(
        string Name,
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

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

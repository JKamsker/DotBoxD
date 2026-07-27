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
    private static readonly byte[] s_rawFrame = CreateRawFrame();
    private static readonly long s_frameChecksum = CalculateChecksum(s_rawFrame);

    public static void Run()
    {
        using var stream = new SynchronousSinkStream();
        var connection = new StreamConnection(stream, ownsStream: false);
        using var sender = new RpcPeerSender(connection, static () => false);
        using var liveTokenSource = new CancellationTokenSource();

        try
        {
            var directFrame = MeasureFrame(
                "Direct frame channel",
                stream,
                connection.SendFrameValueAsync);
            var peerFrame = MeasureFrame(
                "RpcPeerSender frame path",
                stream,
                sender.SendFrameValueAsync);
            var directRawDefault = MeasureRawValueTask(
                "Direct raw/default",
                stream,
                connection.SendValueAsync,
                CancellationToken.None);
            var peerRawDefault = MeasureRawTask(
                "RpcPeerSender raw/default",
                stream,
                sender.SendAsync,
                CancellationToken.None);
            var directRawLive = MeasureRawValueTask(
                "Direct raw/live token",
                stream,
                connection.SendValueAsync,
                liveTokenSource.Token);
            var peerRawLive = MeasureRawTask(
                "RpcPeerSender raw/live token",
                stream,
                sender.SendAsync,
                liveTokenSource.Token);

            Console.WriteLine("RPC peer send-overhead probe");
            Console.WriteLine($"iterations = {Iterations:N0}; warmup = {WarmupIterations:N0}");
            Console.WriteLine(
                $"verified frame bytes = {s_rawFrame.Length}; " +
                $"per-frame checksum = {s_frameChecksum}");
            WritePair("Frame peer overhead", directFrame, peerFrame);
            WritePair("Raw/default peer overhead", directRawDefault, peerRawDefault);
            WritePair("Raw/live peer overhead", directRawLive, peerRawLive);
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static Measurement MeasureFrame(
        string name,
        SynchronousSinkStream stream,
        Func<PooledBufferWriter, CancellationToken, ValueTask> send) =>
        Measure(name, stream, () => SendFrameOnce(send));

    private static Measurement MeasureRawValueTask(
        string name,
        SynchronousSinkStream stream,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> send,
        CancellationToken cancellationToken) =>
        Measure(name, stream, () => SendRawOnce(send, cancellationToken));

    private static Measurement MeasureRawTask(
        string name,
        SynchronousSinkStream stream,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> send,
        CancellationToken cancellationToken) =>
        Measure(name, stream, () => SendRawOnce(send, cancellationToken));

    private static Measurement Measure(
        string name,
        SynchronousSinkStream stream,
        Action send)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            send();
        }

        var before = stream.Snapshot();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < Iterations; i++)
        {
            send();
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        VerifyOutput(name, before, stream.Snapshot());

        return new Measurement(name, elapsed.TotalMilliseconds, allocated);
    }

    private static void SendFrameOnce(Func<PooledBufferWriter, CancellationToken, ValueTask> send)
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

    private static void SendRawOnce(
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> send,
        CancellationToken cancellationToken)
    {
        var pending = send(s_rawFrame, cancellationToken);
        if (!pending.IsCompletedSuccessfully)
        {
            pending.AsTask().GetAwaiter().GetResult();
            throw new InvalidOperationException("The synchronous sink send did not complete synchronously.");
        }

        pending.GetAwaiter().GetResult();
    }

    private static void SendRawOnce(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> send,
        CancellationToken cancellationToken)
    {
        var pending = send(s_rawFrame, cancellationToken);
        if (!pending.IsCompletedSuccessfully)
        {
            pending.GetAwaiter().GetResult();
            throw new InvalidOperationException("The synchronous sink send did not complete synchronously.");
        }

        pending.GetAwaiter().GetResult();
    }

    private static void VerifyOutput(string name, SinkSnapshot before, SinkSnapshot after)
    {
        var writes = after.Writes - before.Writes;
        var bytes = after.Bytes - before.Bytes;
        var checksum = after.Checksum - before.Checksum;
        var expectedBytes = (long)Iterations * s_rawFrame.Length;
        var expectedChecksum = Iterations * s_frameChecksum;
        if (writes != Iterations || bytes != expectedBytes || checksum != expectedChecksum)
        {
            throw new InvalidOperationException(
                $"{name} output mismatch: writes={writes:N0}, bytes={bytes:N0}, checksum={checksum}; " +
                $"expected {Iterations:N0}, {expectedBytes:N0}, {expectedChecksum}.");
        }
    }

    private static byte[] CreateRawFrame()
    {
        using var frame = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 1, MessageType.Request, ReadOnlySpan<byte>.Empty);
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

    private static void WritePair(string overheadName, Measurement direct, Measurement peer)
    {
        Write(direct);
        Write(peer);
        Console.WriteLine(
            $"{overheadName,-31} " +
            $"{peer.NanosecondsPerOperation - direct.NanosecondsPerOperation,8:N1} ns/op " +
            $"{peer.BytesPerOperation - direct.BytesPerOperation,8:N1} B/op");
    }

    private static void Write(Measurement measurement) =>
        Console.WriteLine(
            $"{measurement.Name,-31} {measurement.Milliseconds,8:N1} ms " +
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

    private readonly record struct SinkSnapshot(long Writes, long Bytes, long Checksum);

    private sealed class SynchronousSinkStream : Stream
    {
        private long _bytes;
        private long _checksum;
        private long _writes;

        public SinkSnapshot Snapshot() => new(_writes, _bytes, _checksum);

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
            _writes++;
            _bytes += buffer.Length;
            _checksum += CalculateChecksum(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

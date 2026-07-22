using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Benchmarks.Support.Transport;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class TransportPendingReceiveProbe
{
    private const int FrameLength = 1_024;
    private const int MeasurementIterations = 20_000;
    private const int WarmupIterations = 2_000;

    public static async Task RunAsync()
    {
        var namedPipeFinite = await MeasureNamedPipeAsync(
            "named pipe finite",
            frameReadIdleTimeout: null).ConfigureAwait(false);
        var namedPipeInfinite = await MeasureNamedPipeAsync(
            "named pipe infinite",
            Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        var tcpFinite = await MeasureTcpAsync(
            "TCP finite",
            frameReadIdleTimeout: null).ConfigureAwait(false);
        var tcpInfinite = await MeasureTcpAsync(
            "TCP infinite",
            Timeout.InfiniteTimeSpan).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("Genuinely-pending OS transport receive probe");
        Console.WriteLine(
            "case                    write-to-frame ns    allocated B    B/frame  " +
            "writer B/f  reads/frame  pending/frame");
        Write(namedPipeFinite);
        Write(namedPipeInfinite);
        Write(tcpFinite);
        Write(tcpInfinite);
        Console.WriteLine(
            $"invariants: {MeasurementIterations:N0} frames/lane, every top-level receive pending, " +
            $"{FrameLength:N0} bytes/frame; TCP read-call counters are unavailable through its public API");
    }

    private static async Task<Measurement> MeasureNamedPipeAsync(
        string name,
        TimeSpan? frameReadIdleTimeout)
    {
        var pipeName = $"dotboxd-pending-receive-{Guid.NewGuid():N}";
        using var receiver = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        using var sender = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        var accepting = receiver.WaitForConnectionAsync();
        await sender.ConnectAsync().ConfigureAwait(false);
        await accepting.ConfigureAwait(false);

        var countedReceiver = new CountingReadStream(receiver);
        await using var connection = new StreamConnection(
            countedReceiver,
            ownsStream: false,
            frameReadIdleTimeout: frameReadIdleTimeout);
        using var writer = new GatedFrameWriter(sender, CreateFrame());
        return await MeasureAsync(name, connection, writer, countedReceiver).ConfigureAwait(false);
    }

    private static async Task<Measurement> MeasureTcpAsync(
        string name,
        TimeSpan? frameReadIdleTimeout)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;

        using var sender = new TcpClient(AddressFamily.InterNetwork);
        var accepting = listener.AcceptTcpClientAsync();
        await sender.ConnectAsync(endpoint.Address, endpoint.Port).ConfigureAwait(false);
        using var receiver = await accepting.ConfigureAwait(false);
        sender.NoDelay = true;
        receiver.NoDelay = true;

        await using var connection = new TcpConnection(receiver, frameReadIdleTimeout);
        using var writer = new GatedFrameWriter(sender.GetStream(), CreateFrame());
        return await MeasureAsync(name, connection, writer, readCounter: null).ConfigureAwait(false);
    }

    private static async Task<Measurement> MeasureAsync(
        string name,
        IRpcFrameChannel connection,
        GatedFrameWriter writer,
        CountingReadStream? readCounter)
    {
        await ReadFramesAsync(connection, writer, WarmupIterations).ConfigureAwait(false);

        ForceGc();
        var readsBefore = readCounter?.ReadCount ?? 0;
        var pendingReadsBefore = readCounter?.PendingReadCount ?? 0;
        var writerAllocatedBefore = writer.AllocatedBytes;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var result = await ReadFramesAsync(connection, writer, MeasurementIterations)
            .ConfigureAwait(false);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var writerAllocated = writer.AllocatedBytes - writerAllocatedBefore;
        long? readCount = readCounter is null ? null : readCounter.ReadCount - readsBefore;
        long? pendingReadCount = readCounter is null
            ? null
            : readCounter.PendingReadCount - pendingReadsBefore;

        Validate(result.Checksum, result.PendingReceiveCount, readCount, pendingReadCount);
        return new Measurement(
            name,
            result.CompletionTicks,
            allocated,
            writerAllocated,
            readCount,
            pendingReadCount);
    }

    private static async Task<ReadResult> ReadFramesAsync(
        IRpcFrameChannel connection,
        GatedFrameWriter writer,
        int iterations)
    {
        long checksum = 0;
        long completionTicks = 0;
        var pendingReceiveCount = 0;
        for (var i = 0; i < iterations; i++)
        {
            var pending = connection.ReceiveFrameValueAsync();
            if (pending.IsCompleted)
            {
                throw new InvalidOperationException("The transport receive completed before bytes were released.");
            }

            pendingReceiveCount++;
            writer.ReleaseFrame();
            var frame = await pending.ConfigureAwait(false);
            var completedAt = Stopwatch.GetTimestamp();
            var writeStartedAt = writer.WaitForCompletion();
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

            completionTicks += completedAt - writeStartedAt;
        }

        return new ReadResult(checksum, completionTicks, pendingReceiveCount);
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
        int pendingReceiveCount,
        long? readCount,
        long? pendingReadCount)
    {
        var frame = CreateFrame();
        var checksumPerFrame =
            frame.Length + frame[4] + frame[FrameLength / 2] + frame[^1];
        var expectedChecksum = checksumPerFrame * (long)MeasurementIterations;
        if (checksum != expectedChecksum ||
            pendingReceiveCount != MeasurementIterations ||
            readCount is < MeasurementIterations ||
            pendingReadCount is < MeasurementIterations)
        {
            throw new InvalidOperationException(
                $"Probe invariants failed: checksum {checksum:N0}/{expectedChecksum:N0}, " +
                $"pending receives {pendingReceiveCount:N0}/{MeasurementIterations:N0}, " +
                $"reads {readCount:N0}, pending reads {pendingReadCount:N0}.");
        }
    }

    private static void Write(Measurement measurement)
    {
        var readsPerFrame = measurement.ReadCount / (double?)MeasurementIterations;
        var pendingReadsPerFrame = measurement.PendingReadCount / (double?)MeasurementIterations;
        Console.WriteLine(
            $"{measurement.Name,-22} {measurement.NanosecondsPerFrame,18:N1} " +
            $"{measurement.AllocatedBytes,14:N0} {measurement.BytesPerFrame,10:N1} " +
            $"{measurement.WriterBytesPerFrame,10:N1} {readsPerFrame,12:N1} " +
            $"{pendingReadsPerFrame,14:N1}");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct ReadResult(
        long Checksum,
        long CompletionTicks,
        int PendingReceiveCount);

    private readonly record struct Measurement(
        string Name,
        long CompletionTicks,
        long AllocatedBytes,
        long WriterAllocatedBytes,
        long? ReadCount,
        long? PendingReadCount)
    {
        public double BytesPerFrame => AllocatedBytes / (double)MeasurementIterations;

        public double WriterBytesPerFrame => WriterAllocatedBytes / (double)MeasurementIterations;

        public double NanosecondsPerFrame =>
            CompletionTicks * (1_000_000_000d / Stopwatch.Frequency) / MeasurementIterations;
    }
}

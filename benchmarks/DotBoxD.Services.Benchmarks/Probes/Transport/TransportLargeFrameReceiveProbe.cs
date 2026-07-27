using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Benchmarks.Support.Transport;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class TransportLargeFrameReceiveProbe
{
    private const int FrameLength = 256 * 1024;
    private const int MeasurementIterations = 1_000;
    private const int WarmupIterations = 100;

    public static async Task RunAsync()
    {
        var namedPipe = await MeasureNamedPipeAsync(countReads: false).ConfigureAwait(false);
        var countedNamedPipe = await MeasureNamedPipeAsync(countReads: true).ConfigureAwait(false);
        var tcp = await MeasureTcpAsync().ConfigureAwait(false);

        Console.WriteLine("Large pending OS transport receive probe");
        Console.WriteLine(
            "transport               total ms       us/frame    allocated B    B/frame  reads/frame");
        Write(namedPipe);
        Write(countedNamedPipe);
        Write(tcp);
        Console.WriteLine(
            $"invariants: {MeasurementIterations:N0} pending frames/lane, " +
            $"{FrameLength / 1024:N0} KiB/frame");
    }

    private static async Task<Measurement> MeasureNamedPipeAsync(bool countReads)
    {
        var pipeName = $"dotboxd-large-receive-{Guid.NewGuid():N}";
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

        Stream readStream = countReads ? new CountingReadStream(receiver) : receiver;
        var readCounter = readStream as CountingReadStream;
        await using var connection = new StreamConnection(
            readStream,
            ownsStream: true,
            frameReadIdleTimeout: Timeout.InfiniteTimeSpan);
        return await MeasureAsync(
            countReads ? "named pipe counted" : "named pipe direct",
            connection,
            sender,
            readCounter).ConfigureAwait(false);
    }

    private static async Task<Measurement> MeasureTcpAsync()
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

        await using var connection = new TcpConnection(receiver, Timeout.InfiniteTimeSpan);
        return await MeasureAsync(
            "TCP",
            connection,
            sender.GetStream(),
            readCounter: null).ConfigureAwait(false);
    }

    private static async Task<Measurement> MeasureAsync(
        string name,
        IRpcFrameChannel connection,
        Stream writer,
        CountingReadStream? readCounter)
    {
        var frame = CreateFrame();
        _ = await ExchangeFramesAsync(connection, writer, frame, WarmupIterations).ConfigureAwait(false);

        ForceGc();
        var readsBefore = readCounter?.ReadCount ?? 0;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var startedAt = Stopwatch.GetTimestamp();
        var result = await ExchangeFramesAsync(
            connection,
            writer,
            frame,
            MeasurementIterations).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        long? readCount = readCounter is null ? null : readCounter.ReadCount - readsBefore;

        Validate(result, readCount, frame);
        return new Measurement(name, elapsed.TotalMilliseconds, allocated, readCount);
    }

    private static async Task<long> ExchangeFramesAsync(
        IRpcFrameChannel connection,
        Stream writer,
        byte[] frame,
        int iterations)
    {
        long checksum = 0;
        for (var index = 0; index < iterations; index++)
        {
            var pending = connection.ReceiveFrameValueAsync();
            if (pending.IsCompleted)
            {
                throw new InvalidOperationException("The large-frame receive completed before its write.");
            }

            var write = writer.WriteAsync(frame);
            var received = await pending.ConfigureAwait(false);
            await write.ConfigureAwait(false);
            try
            {
                checksum += received.Length + received.Memory.Span[4] + received.Memory.Span[^1];
            }
            finally
            {
                received.Dispose();
            }
        }

        return checksum;
    }

    private static byte[] CreateFrame()
    {
        var frame = new byte[FrameLength];
        BinaryPrimitives.WriteInt32LittleEndian(frame, FrameLength);
        for (var index = sizeof(int); index < frame.Length; index++)
        {
            frame[index] = unchecked((byte)(index * 31));
        }

        return frame;
    }

    private static void Validate(long checksum, long? readCount, byte[] frame)
    {
        var expected = (frame.Length + frame[4] + frame[^1]) * (long)MeasurementIterations;
        if (checksum != expected || readCount is <= 0)
        {
            throw new InvalidOperationException(
                $"Probe invariants failed: checksum {checksum:N0}/{expected:N0}, " +
                $"reads {readCount?.ToString("N0") ?? "unavailable"}.");
        }
    }

    private static void Write(Measurement measurement)
    {
        var readsPerFrame = measurement.ReadCount / (double?)MeasurementIterations;
        Console.WriteLine(
            $"{measurement.Name,-22} {measurement.ElapsedMilliseconds,10:N1} " +
            $"{measurement.ElapsedMilliseconds * 1_000 / MeasurementIterations,14:N1} " +
            $"{measurement.AllocatedBytes,14:N0} " +
            $"{measurement.AllocatedBytes / (double)MeasurementIterations,10:N1} " +
            $"{readsPerFrame,12:N3}");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(
        string Name,
        double ElapsedMilliseconds,
        long AllocatedBytes,
        long? ReadCount);
}

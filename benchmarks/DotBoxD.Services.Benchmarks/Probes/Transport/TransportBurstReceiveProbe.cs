using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Benchmarks.Support.Transport;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using static DotBoxD.Services.Benchmarks.Probes.TransportBurstReceiveProbeSupport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class TransportBurstReceiveProbe
{
    private const int FrameLength = 1_024;
    private const int MeasurementBatches = 50_000;
    private const int WarmupBatches = 2_000;
    private static readonly int BatchSize = TransportReceiveProbeOptions.BatchSize;

    public static async Task RunAsync()
    {
        var namedPipeBufferedDirect = await MeasureNamedPipeAsync(
            "pipe buffered direct",
            Timeout.InfiniteTimeSpan,
            startReceiveBeforeWrite: false,
            countReads: false).ConfigureAwait(false);
        var namedPipeBufferedCounted = await MeasureNamedPipeAsync(
            "pipe buffered counted",
            Timeout.InfiniteTimeSpan,
            startReceiveBeforeWrite: false,
            countReads: true).ConfigureAwait(false);
        var namedPipeBufferedFiniteDirect = await MeasureNamedPipeAsync(
            "pipe buffered finite direct",
            frameReadIdleTimeout: null,
            startReceiveBeforeWrite: false,
            countReads: false).ConfigureAwait(false);
        var namedPipeBufferedFiniteCounted = await MeasureNamedPipeAsync(
            "pipe buffered finite counted",
            frameReadIdleTimeout: null,
            startReceiveBeforeWrite: false,
            countReads: true).ConfigureAwait(false);
        var namedPipePendingFiniteDirect = await MeasureNamedPipeAsync(
            "pipe pending finite direct",
            frameReadIdleTimeout: null,
            startReceiveBeforeWrite: true,
            countReads: false).ConfigureAwait(false);
        var namedPipePendingFiniteCounted = await MeasureNamedPipeAsync(
            "pipe pending finite counted",
            frameReadIdleTimeout: null,
            startReceiveBeforeWrite: true,
            countReads: true).ConfigureAwait(false);
        var namedPipePendingInfiniteCounted = await MeasureNamedPipeAsync(
            "pipe pending infinite counted",
            Timeout.InfiniteTimeSpan,
            startReceiveBeforeWrite: true,
            countReads: true).ConfigureAwait(false);
        var tcpBuffered = await MeasureTcpAsync(
            "TCP buffered",
            Timeout.InfiniteTimeSpan,
            startReceiveBeforeWrite: false).ConfigureAwait(false);
        var tcpBufferedFinite = await MeasureTcpAsync(
            "TCP buffered finite",
            frameReadIdleTimeout: null,
            startReceiveBeforeWrite: false).ConfigureAwait(false);
        var tcpPendingFinite = await MeasureTcpAsync(
            "TCP pending finite",
            frameReadIdleTimeout: null,
            startReceiveBeforeWrite: true).ConfigureAwait(false);
        var tcpPendingInfinite = await MeasureTcpAsync(
            "TCP pending infinite",
            Timeout.InfiniteTimeSpan,
            startReceiveBeforeWrite: true).ConfigureAwait(false);

        Console.WriteLine("Buffered OS transport receive probe");
        Console.WriteLine(
            "transport                  total ms       ns/frame    allocated B    B/frame  " +
            "reads/frame  pending/frame");
        Write(namedPipeBufferedDirect, MeasurementBatches, BatchSize);
        Write(namedPipeBufferedCounted, MeasurementBatches, BatchSize);
        Write(namedPipeBufferedFiniteDirect, MeasurementBatches, BatchSize);
        Write(namedPipeBufferedFiniteCounted, MeasurementBatches, BatchSize);
        Write(namedPipePendingFiniteDirect, MeasurementBatches, BatchSize);
        Write(namedPipePendingFiniteCounted, MeasurementBatches, BatchSize);
        Write(namedPipePendingInfiniteCounted, MeasurementBatches, BatchSize);
        Write(tcpBuffered, MeasurementBatches, BatchSize);
        Write(tcpBufferedFinite, MeasurementBatches, BatchSize);
        Write(tcpPendingFinite, MeasurementBatches, BatchSize);
        Write(tcpPendingInfinite, MeasurementBatches, BatchSize);
        Console.WriteLine(
            $"invariants: {MeasurementBatches * BatchSize:N0} frames/lane, " +
            $"{BatchSize} frames/write, {FrameLength:N0} bytes/frame");
    }

    private static async Task<Measurement> MeasureNamedPipeAsync(
        string name,
        TimeSpan? frameReadIdleTimeout,
        bool startReceiveBeforeWrite,
        bool countReads)
    {
        var pipeName = $"dotboxd-burst-receive-{Guid.NewGuid():N}";
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
        var countedReceiver = readStream as CountingReadStream;
        await using var connection = new StreamConnection(
            readStream,
            ownsStream: true,
            frameReadIdleTimeout: frameReadIdleTimeout);
        return await MeasureAsync(
            name,
            connection,
            sender,
            countedReceiver,
            startReceiveBeforeWrite).ConfigureAwait(false);
    }

    private static async Task<Measurement> MeasureTcpAsync(
        string name,
        TimeSpan? frameReadIdleTimeout,
        bool startReceiveBeforeWrite)
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
        return await MeasureAsync(
            name,
            connection,
            sender.GetStream(),
            readCounter: null,
            startReceiveBeforeWrite).ConfigureAwait(false);
    }

    private static async Task<Measurement> MeasureAsync(
        string name,
        IRpcFrameChannel connection,
        Stream writer,
        CountingReadStream? readCounter,
        bool startReceiveBeforeWrite)
    {
        var batch = CreateBatch(FrameLength, BatchSize);
        await ExchangeBatchesAsync(
            connection,
            writer,
            batch,
            WarmupBatches,
            startReceiveBeforeWrite).ConfigureAwait(false);

        ForceGc();
        var readsBefore = readCounter?.ReadCount ?? 0;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var startedAt = Stopwatch.GetTimestamp();
        var result = await ExchangeBatchesAsync(
            connection,
            writer,
            batch,
            MeasurementBatches,
            startReceiveBeforeWrite).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        long? readCount = readCounter is null ? null : readCounter.ReadCount - readsBefore;

        Validate(
            result,
            readCount,
            startReceiveBeforeWrite,
            MeasurementBatches,
            BatchSize,
            FrameLength);
        return new Measurement(name, elapsed.TotalMilliseconds, allocated, readCount, result.PendingReceives);
    }

    private static async Task<ExchangeResult> ExchangeBatchesAsync(
        IRpcFrameChannel connection,
        Stream writer,
        byte[] batch,
        int batches,
        bool startReceiveBeforeWrite)
    {
        long checksum = 0;
        var pendingReceives = 0;
        for (var batchIndex = 0; batchIndex < batches; batchIndex++)
        {
            var firstPending = startReceiveBeforeWrite
                ? connection.ReceiveFrameValueAsync()
                : default;
            if (startReceiveBeforeWrite && firstPending.IsCompleted)
            {
                throw new InvalidOperationException("The gated batch receive completed before its write.");
            }

            await writer.WriteAsync(batch).ConfigureAwait(false);
            var firstFrameIndex = 0;
            if (startReceiveBeforeWrite)
            {
                pendingReceives++;
                checksum += Consume(await firstPending.ConfigureAwait(false));
                firstFrameIndex = 1;
            }

            for (var frameIndex = firstFrameIndex; frameIndex < BatchSize; frameIndex++)
            {
                var pending = connection.ReceiveFrameValueAsync();
                if (!pending.IsCompletedSuccessfully)
                {
                    pendingReceives++;
                }

                checksum += Consume(await pending.ConfigureAwait(false));
            }
        }

        return new ExchangeResult(checksum, pendingReceives);
    }

    private static int Consume(RpcFrame frame)
    {
        try
        {
            return frame.Length + frame.Memory.Span[4] + frame.Memory.Span[^1];
        }
        finally
        {
            frame.Dispose();
        }
    }

}

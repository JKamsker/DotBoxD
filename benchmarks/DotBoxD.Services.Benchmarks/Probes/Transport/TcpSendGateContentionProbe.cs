using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Transports.Tcp;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class TcpSendGateContentionProbe
{
    private const int Iterations = 10_000;
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(15);

    public static async Task RunAsync()
    {
        var rawDefault = await MeasureAsync(CancellationToken.None, ownedFrames: false).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        var rawLive = await MeasureAsync(cancellation.Token, ownedFrames: false).ConfigureAwait(false);
        var ownedDefault = await MeasureAsync(CancellationToken.None, ownedFrames: true).ConfigureAwait(false);
        var ownedLive = await MeasureAsync(cancellation.Token, ownedFrames: true).ConfigureAwait(false);

        Console.WriteLine("TCP contended send-gate admission probe");
        Console.WriteLine("path/token             enqueue ns/waiter   enqueue B/waiter");
        Write("raw/default", rawDefault);
        Write("raw/live reusable", rawLive);
        Write("owned/default", ownedDefault);
        Write("owned/live reusable", ownedLive);
        Console.WriteLine($"invariants: {Iterations:N0} queued sends behind one backpressured TCP write/lane");
    }

    private static async Task<Measurement> MeasureAsync(
        CancellationToken cancellationToken,
        bool ownedFrames)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var receiver = new TcpClient(AddressFamily.InterNetwork)
        {
            ReceiveBufferSize = 1_024,
        };
        var accepting = listener.AcceptTcpClientAsync();
        await receiver.ConnectAsync(endpoint.Address, endpoint.Port).ConfigureAwait(false);
        using var sender = await accepting.ConfigureAwait(false);
        sender.SendBufferSize = 1_024;
        await using var connection = new TcpConnection(sender, Timeout.InfiniteTimeSpan);

        var blockingSend = connection.SendValueAsync(CreateFrame(MessageFramer.MaxMessageSize)).AsTask();
        if (blockingSend.IsCompleted)
        {
            throw new InvalidOperationException("The saturating TCP send did not retain the send gate.");
        }

        var waitingSends = new Task[Iterations];
        var frame = CreateFrame(MessageFramer.HeaderSize);
        var frameOwners = ownedFrames ? CreateOwnedFrames() : null;
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        for (var index = 0; index < waitingSends.Length; index++)
        {
            waitingSends[index] = frameOwners is null
                ? connection.SendValueAsync(frame, cancellationToken).AsTask()
                : connection.SendFrameValueAsync(frameOwners[index], cancellationToken).AsTask();
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (Array.Exists(waitingSends, static send => send.IsCompleted))
        {
            throw new InvalidOperationException("A contended send completed before the gate owner was released.");
        }

        await connection.DisposeAsync().ConfigureAwait(false);
        await ObserveCompletionAsync(blockingSend).ConfigureAwait(false);
        foreach (var waitingSend in waitingSends)
        {
            try
            {
                await waitingSend.WaitAsync(CleanupTimeout).ConfigureAwait(false);
                throw new InvalidOperationException("A queued send succeeded after connection disposal.");
            }
            catch (ObjectDisposedException)
            {
            }
        }
        if (frameOwners is not null)
        {
            AssertDisposed(frameOwners[0]);
        }

        return new Measurement(elapsed, allocated);
    }

    private static async Task ObserveCompletionAsync(Task task)
    {
        try
        {
            await task.WaitAsync(CleanupTimeout).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (IOException)
        {
            return;
        }
        catch (SocketException)
        {
            return;
        }
    }

    private static byte[] CreateFrame(int length)
    {
        var frame = new byte[length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), 1);
        frame[8] = (byte)MessageType.Request;
        return frame;
    }

    private static PooledBufferWriter[] CreateOwnedFrames()
    {
        var frames = new PooledBufferWriter[Iterations];
        for (var index = 0; index < frames.Length; index++)
        {
            var frame = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
            MessageFramer.WriteFrame(frame, 1, MessageType.Request, ReadOnlySpan<byte>.Empty);
            frames[index] = frame;
        }

        return frames;
    }

    private static void AssertDisposed(PooledBufferWriter frame)
    {
        try
        {
            _ = frame.WrittenMemory;
            throw new InvalidOperationException("A queued owned frame was not disposed.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(string name, Measurement measurement) =>
        Console.WriteLine(
            $"{name,-20} {measurement.NanosecondsPerWaiter,12:N1} " +
            $"{measurement.BytesPerWaiter,14:N1}");

    private readonly record struct Measurement(TimeSpan Elapsed, long AllocatedBytes)
    {
        public double NanosecondsPerWaiter => Elapsed.TotalNanoseconds / Iterations;

        public double BytesPerWaiter => AllocatedBytes / (double)Iterations;
    }
}

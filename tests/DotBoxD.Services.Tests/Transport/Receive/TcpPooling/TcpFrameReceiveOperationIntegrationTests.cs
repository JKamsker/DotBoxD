using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.TcpPooling;

public sealed class TcpFrameReceiveOperationIntegrationTests
{
    private static readonly AsyncLocal<string?> Context = new();
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PendingPrefixThenPendingBody_CompletesFrameAndSuccessor()
    {
        await using var pair = await TcpReceiveTestPair.CreateAsync();
        var firstBytes = TcpReceiveTestPair.CreateFrame(messageId: 401);
        var first = pair.Connection.ReceiveFrameValueAsync();

        Assert.False(first.IsCompleted);
        await pair.QueueBytesAsync(firstBytes.AsMemory(0, sizeof(int)));
        await pair.WaitForPrefixAsync(firstBytes.Length);
        Assert.False(first.IsCompleted);

        await pair.QueueBytesAsync(firstBytes.AsMemory(sizeof(int)));
        using (var frame = await first.AsTask().WaitAsync(Guard))
        {
            AssertFrame(frame, messageId: 401);
        }

        var secondBytes = TcpReceiveTestPair.CreateFrame(messageId: 402);
        var second = pair.Connection.ReceiveFrameValueAsync();
        Assert.False(second.IsCompleted);
        await pair.QueueBytesAsync(secondBytes);
        using var secondFrame = await second.AsTask().WaitAsync(Guard);
        AssertFrame(secondFrame, messageId: 402);
    }

    [Fact]
    public async Task RawInlineConsumer_CanStartSuccessorBeforeProducerUnwinds()
    {
        var previous = Context.Value;
        Context.Value = "caller";
        try
        {
            await using var pair = await TcpReceiveTestPair.CreateAsync();
            var first = pair.Connection.ReceiveFrameValueAsync();
            var reentered = new TaskCompletionSource<ValueTask<RpcFrame>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable xUnit1030 // This regression intentionally registers a raw inline continuation.
            var awaiter = first.ConfigureAwait(false).GetAwaiter();
#pragma warning restore xUnit1030
            awaiter.UnsafeOnCompleted(() =>
            {
                try
                {
                    Assert.Equal("caller", Context.Value);
                    using var frame = awaiter.GetResult();
                    AssertFrame(frame, messageId: 403);
                    reentered.TrySetResult(pair.Connection.ReceiveFrameValueAsync());
                }
                catch (Exception error)
                {
                    reentered.TrySetException(error);
                }
            });

            await Task.Run(async () =>
            {
                Context.Value = "producer";
                await pair.QueueBytesAsync(TcpReceiveTestPair.CreateFrame(messageId: 403));
            });

            var second = await reentered.Task.WaitAsync(Guard);
            Assert.False(second.IsCompleted);
            await pair.QueueBytesAsync(TcpReceiveTestPair.CreateFrame(messageId: 404));
            using var secondFrame = await second.AsTask().WaitAsync(Guard);
            AssertFrame(secondFrame, messageId: 404);
        }
        finally
        {
            Context.Value = previous;
        }
    }

    [Fact]
    public async Task CompletedUnconsumedValueTask_DoesNotRetainConnectionGraph()
    {
        var probe = CreateCompletedUnconsumedReceive();

        ForceGc();

        Assert.False(probe.Connection.IsAlive);
        Assert.False(probe.Client.IsAlive);
        Assert.False(probe.Stream.IsAlive);
        Assert.False(probe.CallerCancellation.IsAlive);
        using var frame = await probe.Pending;
        AssertFrame(frame, messageId: 405);
    }

    [Fact]
    public async Task PartialPrefixAndBodyEof_PreserveBodyRelativeDiagnostics()
    {
        await using (var prefixPair = await TcpReceiveTestPair.CreateAsync())
        {
            var pending = prefixPair.Connection.ReceiveFrameValueAsync();
            await prefixPair.QueueBytesAsync(new byte[] { 1, 2 });
            prefixPair.Peer.Client.Shutdown(SocketShutdown.Send);

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => pending.AsTask().WaitAsync(Guard));
            Assert.Equal("Connection closed after 2 of 4 frame length bytes.", error.Message);
        }

        await using var bodyPair = await TcpReceiveTestPair.CreateAsync();
        const int totalLength = 12;
        var partial = new byte[sizeof(int) + 1];
        BinaryPrimitives.WriteInt32LittleEndian(partial, totalLength);
        partial[^1] = 0x2A;
        var bodyPending = bodyPair.Connection.ReceiveFrameValueAsync();
        await bodyPair.QueueBytesAsync(partial);
        bodyPair.Peer.Client.Shutdown(SocketShutdown.Send);

        var bodyError = await Assert.ThrowsAsync<InvalidDataException>(
            () => bodyPending.AsTask().WaitAsync(Guard));
        Assert.Equal("Connection closed after 1 of 8 frame bytes.", bodyError.Message);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetentionProbe CreateCompletedUnconsumedReceive()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var peer = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        peer.Connect(endpoint);
        var client = listener.AcceptTcpClient();
        client.NoDelay = true;
        var stream = client.GetStream();
        var callerCancellation = new CancellationTokenSource();
        var connection = new TcpConnection(client, Timeout.InfiniteTimeSpan);
        var pending = connection.ReceiveFrameValueAsync(callerCancellation.Token);
        peer.GetStream().Write(TcpReceiveTestPair.CreateFrame(messageId: 405));

        var deadline = DateTime.UtcNow + Guard;
        while (!pending.IsCompleted)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The TCP receive did not complete.");
            }

            Thread.Yield();
        }

        peer.Dispose();
        return new RetentionProbe(
            pending,
            new WeakReference(connection),
            new WeakReference(client),
            new WeakReference(stream),
            new WeakReference(callerCancellation));
    }

    private static void AssertFrame(RpcFrame frame, int messageId)
    {
        Assert.True(MessageFramer.TryReadFrameHeader(frame.Memory, out var actual, out var type));
        Assert.Equal(messageId, actual);
        Assert.Equal(MessageType.Response, type);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record RetentionProbe(
        ValueTask<RpcFrame> Pending,
        WeakReference Connection,
        WeakReference Client,
        WeakReference Stream,
        WeakReference CallerCancellation);
}

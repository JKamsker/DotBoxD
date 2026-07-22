using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.LookaheadLifecycle;

public sealed class TcpConnectionLookaheadLifecycleTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(2);

    private static readonly FieldInfo ReceiveBufferField =
        typeof(TcpConnection).GetField(
            "_receiveBuffer",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("TcpConnection._receiveBuffer was not found.");

    [Fact]
    public async Task ReceiveAsync_DrainedFrameReturnsLookaheadWindow()
    {
        await using var pair = await ConnectedPair.CreateAsync();
        var expected = CreateFrame(messageId: 301);

        await pair.Peer.GetStream().WriteAsync(expected);
        using var received = await pair.Connection.ReceiveAsync().WaitAsync(Guard);

        Assert.Equal(expected, received.Memory.ToArray());
        Assert.False(GetReceiveBuffer(pair.Connection).HasBuffer);
    }

    [Fact]
    public async Task IdlePendingPrefixRead_DoesNotRentLookaheadWindow()
    {
        await using var pair = await ConnectedPair.CreateAsync();
        var receive = pair.Connection.ReceiveFrameValueAsync().AsTask();

        Assert.False(receive.IsCompleted);
        Assert.False(GetReceiveBuffer(pair.Connection).HasBuffer);

        await pair.Connection.DisposeAsync();
        _ = await Record.ExceptionAsync(() => receive.WaitAsync(Guard));

        Assert.False(GetReceiveBuffer(pair.Connection).HasBuffer);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => pair.Connection.ReceiveAsync());
    }

    [Fact]
    public async Task DisposeAsync_DuringPendingBodyLookaheadReturnsWindowAfterReceiveExit()
    {
        await using var pair = await ConnectedPair.CreateAsync();
        var expected = CreateFrame(messageId: 302);
        await pair.Peer.GetStream().WriteAsync(
            expected.AsMemory(0, StreamFrameReadOperations.LengthPrefixSize));
        await WaitForAvailableBytesAsync(
            pair.Receiver,
            StreamFrameReadOperations.LengthPrefixSize);

        var receive = pair.Connection.ReceiveFrameValueAsync().AsTask();

        await WaitForRentedBufferAsync(pair.Connection);
        Assert.True(GetReceiveBuffer(pair.Connection).HasBuffer);

        await pair.Connection.DisposeAsync();
        _ = await Record.ExceptionAsync(() => receive.WaitAsync(Guard));

        Assert.False(GetReceiveBuffer(pair.Connection).HasBuffer);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => pair.Connection.ReceiveAsync());
    }

    private static async Task WaitForAvailableBytesAsync(TcpClient receiver, int expected)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (receiver.Available < expected)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("TCP prefix did not reach the receiver.");
            }

            await Task.Yield();
        }
    }

    private static async Task WaitForRentedBufferAsync(TcpConnection connection)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (!GetReceiveBuffer(connection).HasBuffer)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("TCP receive did not rent its lookahead window.");
            }

            await Task.Yield();
        }
    }

    private static StreamFrameReceiveBuffer GetReceiveBuffer(TcpConnection connection) =>
        (StreamFrameReceiveBuffer)(ReceiveBufferField.GetValue(connection)
            ?? throw new InvalidOperationException("TcpConnection._receiveBuffer is null."));

    private static byte[] CreateFrame(int messageId)
    {
        using var frame = MessageFramer.FrameToPayload(
            messageId,
            MessageType.Response,
            new byte[] { 1, 2, 3, 4 });
        return frame.Memory.ToArray();
    }

    private sealed class ConnectedPair : IAsyncDisposable
    {
        private ConnectedPair(TcpConnection connection, TcpClient receiver, TcpClient peer)
        {
            Connection = connection;
            Receiver = receiver;
            Peer = peer;
        }

        public TcpConnection Connection { get; }

        public TcpClient Peer { get; }

        public TcpClient Receiver { get; }

        public static async Task<ConnectedPair> CreateAsync()
        {
            using var listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var peer = new TcpClient(AddressFamily.InterNetwork);
            try
            {
                var accepting = listener.AcceptTcpClientAsync();
                await peer.ConnectAsync(endpoint.Address, endpoint.Port);
                var accepted = await accepting;
                return new ConnectedPair(
                    new TcpConnection(accepted, Timeout.InfiniteTimeSpan),
                    accepted,
                    peer);
            }
            catch
            {
                peer.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            Peer.Dispose();
        }
    }
}

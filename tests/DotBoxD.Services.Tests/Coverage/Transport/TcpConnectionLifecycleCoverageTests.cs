using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Transport.TcpTransportCoverageTestHelpers;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class TcpConnectionLifecycleCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SendAsync_AfterDispose_ThrowsObjectDisposed()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        var serverConn = await acceptTask.WaitAsync(Timeout);
        var frameChannel = Assert.IsAssignableFrom<IRpcFrameChannel>(serverConn);

        await serverConn.DisposeAsync();

        using var frame = BuildFrame(messageId: 1, type: 1, bodyLength: 4);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => serverConn.SendAsync(frame.Memory).WaitAsync(Timeout));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var ownedFrame = CreateOwnedFrame();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => frameChannel.SendFrameValueAsync(ownedFrame, cancellation.Token).AsTask());
        AssertDisposed(ownedFrame);
    }

    [Fact]
    public async Task SendFrameValueAsync_RoundTripsAndDisposesOwnedWriter()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);
        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await using var serverConn = await acceptTask.WaitAsync(Timeout);
        var frameChannel = Assert.IsAssignableFrom<IRpcFrameChannel>(serverConn);
        var frame = CreateOwnedFrame();
        var expected = frame.WrittenMemory.ToArray();

        await frameChannel.SendFrameValueAsync(frame).AsTask().WaitAsync(Timeout);

        AssertDisposed(frame);
        var received = new byte[expected.Length];
        await rawClient.GetStream().ReadExactlyAsync(received).AsTask().WaitAsync(Timeout);
        Assert.Equal(expected, received);
    }

    [Fact]
    public async Task ReceiveAsync_AfterDispose_ThrowsObjectDisposed()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        var serverConn = await acceptTask.WaitAsync(Timeout);

        await serverConn.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => serverConn.ReceiveAsync().WaitAsync(Timeout));
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotentAndMarksDisconnected()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        using var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        var serverConn = await acceptTask.WaitAsync(Timeout);

        Assert.True(serverConn.IsConnected);

        await serverConn.DisposeAsync();
        await serverConn.DisposeAsync();

        Assert.False(serverConn.IsConnected);
    }

    [Fact]
    public async Task ReceiveAsync_AfterRemotePeerDisconnects_ReturnsEmptyAndDisposeFlipsIsConnected()
    {
        await using var server = new TcpServerTransport(IPAddress.Loopback, 0);
        await server.StartAsync().WaitAsync(Timeout);
        var port = RequirePort(server);

        var rawClient = new TcpClient();
        var acceptTask = server.AcceptAsync();
        await rawClient.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
        await using var serverConn = await acceptTask.WaitAsync(Timeout);

        Assert.True(serverConn.IsConnected);
        Assert.NotEqual("unknown", serverConn.RemoteEndpoint);

        rawClient.Close();
        using var received = await serverConn.ReceiveAsync().WaitAsync(Timeout);
        Assert.Equal(0, received.Length);

        using var received2 = await serverConn.ReceiveAsync().WaitAsync(Timeout);
        Assert.Equal(0, received2.Length);

        await serverConn.DisposeAsync();
        Assert.False(serverConn.IsConnected);
    }

    private static PooledBufferWriter CreateOwnedFrame()
    {
        var frame = new PooledBufferWriter(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        return frame;
    }

    private static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);
}

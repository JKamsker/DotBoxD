using System.Net;
using System.Net.Sockets;
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

        await serverConn.DisposeAsync();

        using var frame = BuildFrame(messageId: 1, type: 1, bodyLength: 4);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => serverConn.SendAsync(frame.Memory).WaitAsync(Timeout));
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
}

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class ConnectionSendCancellationOrderingRegressionTests
{
    [Fact]
    public async Task StreamConnection_SendAsync_WithPreCanceledTokenCancelsBeforeUndersizedFrameValidation()
    {
        await using var connection = new StreamConnection(new MemoryStream(), ownsStream: false);
        using var cts = PreCanceledTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => connection.SendAsync(CreateUndersizedFrame(), cts.Token));
    }

    [Fact]
    public async Task StreamConnection_SendValueAsync_WithPreCanceledTokenCancelsBeforeUndefinedTypeValidation()
    {
        await using var connection = new StreamConnection(new MemoryStream(), ownsStream: false);
        using var cts = PreCanceledTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await connection.SendValueAsync(CreateUndefinedMessageTypeFrame(), cts.Token));
    }

    [Fact]
    public async Task StreamConnection_OwnedFrame_WithPreCanceledTokenCancelsAndDisposesBeforeValidation()
    {
        await using var connection = new StreamConnection(new MemoryStream(), ownsStream: false);
        using var cts = PreCanceledTokenSource();
        var frame = CreateUndefinedMessageTypeOwnedFrame();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => connection.SendFrameValueAsync(frame, cts.Token).AsTask());

        AssertDisposed(frame);
    }

    [Fact]
    public async Task TcpConnection_SendAsync_WithPreCanceledTokenCancelsBeforeUndersizedFrameValidation()
    {
        await using var pair = await ConnectedTcpPair.CreateAsync();
        using var cts = PreCanceledTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => pair.Server.SendAsync(CreateUndersizedFrame(), cts.Token));
    }

    [Fact]
    public async Task TcpConnection_SendValueAsync_WithPreCanceledTokenCancelsBeforeUndefinedTypeValidation()
    {
        await using var pair = await ConnectedTcpPair.CreateAsync();
        using var cts = PreCanceledTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await pair.Server.SendValueAsync(CreateUndefinedMessageTypeFrame(), cts.Token));
    }

    [Fact]
    public async Task TcpConnection_OwnedFrame_WithPreCanceledTokenCancelsAndDisposesBeforeValidation()
    {
        await using var pair = await ConnectedTcpPair.CreateAsync();
        using var cts = PreCanceledTokenSource();
        var frame = CreateUndefinedMessageTypeOwnedFrame();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => pair.Server.SendFrameValueAsync(frame, cts.Token).AsTask());

        AssertDisposed(frame);
    }

    private static CancellationTokenSource PreCanceledTokenSource()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts;
    }

    private static byte[] CreateUndersizedFrame() => new byte[MessageFramer.HeaderSize - 1];

    private static byte[] CreateUndefinedMessageTypeFrame()
    {
        var frame = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(frame, frame.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), 1);
        frame[8] = 0x7F;
        return frame;
    }

    private static PooledBufferWriter CreateUndefinedMessageTypeOwnedFrame()
    {
        var bytes = CreateUndefinedMessageTypeFrame();
        var frame = new PooledBufferWriter(bytes.Length);
        bytes.CopyTo(frame.GetSpan(bytes.Length));
        frame.Advance(bytes.Length);
        return frame;
    }

    private static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);

    private sealed class ConnectedTcpPair : IAsyncDisposable
    {
        private readonly TcpClient _client;

        private ConnectedTcpPair(TcpConnection server, TcpClient client)
        {
            Server = server;
            _client = client;
        }

        public TcpConnection Server { get; }

        public static async Task<ConnectedTcpPair> CreateAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var client = new TcpClient();
            var acceptTask = listener.AcceptTcpClientAsync();
            await client.ConnectAsync(IPAddress.Loopback, port);
            var serverClient = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));
            listener.Stop();

            return new ConnectedTcpPair(new TcpConnection(serverClient), client);
        }

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            _client.Dispose();
        }
    }
}

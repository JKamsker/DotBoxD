using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.TcpPooling;

internal sealed class TcpSendTestPair : IAsyncDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private TcpSendTestPair(TcpConnection connection, TcpClient peer)
    {
        Connection = connection;
        Peer = peer;
    }

    public TcpConnection Connection { get; }

    public TcpClient Peer { get; }

    public static async Task<TcpSendTestPair> CreateAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var peer = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            var accepting = listener.AcceptTcpClientAsync();
            await peer.ConnectAsync(endpoint.Address, endpoint.Port).WaitAsync(Guard);
            var sender = await accepting.WaitAsync(Guard);
            sender.NoDelay = true;
            return new TcpSendTestPair(new TcpConnection(sender), peer);
        }
        catch
        {
            peer.Dispose();
            throw;
        }
    }

    public async Task<byte[]> ReadAsync(int length)
    {
        var bytes = new byte[length];
        var read = 0;
        using var cancellation = new CancellationTokenSource(Guard);
        while (read < bytes.Length)
        {
            var count = await Peer.GetStream()
                .ReadAsync(bytes.AsMemory(read), cancellation.Token);
            if (count == 0)
            {
                throw new InvalidDataException(
                    $"The TCP peer closed after {read} of {length} bytes.");
            }

            read += count;
        }

        return bytes;
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        Peer.Dispose();
    }
}

internal static class TcpSendTestFrames
{
    public static byte[] CreateBytes(int messageId, int bodyLength = 8)
    {
        var body = Enumerable.Range(0, bodyLength)
            .Select(index => unchecked((byte)(messageId + index)))
            .ToArray();
        using var payload = MessageFramer.FrameToPayload(
            messageId,
            MessageType.Request,
            body);
        return payload.Memory.ToArray();
    }

    public static PooledBufferWriter CreateOwned(int messageId, int bodyLength = 8)
    {
        var bytes = CreateBytes(messageId, bodyLength);
        var frame = new PooledBufferWriter(bytes.Length);
        bytes.CopyTo(frame.GetSpan(bytes.Length));
        frame.Advance(bytes.Length);
        return frame;
    }

    public static PooledBufferWriter CreatePooled(int messageId, int bodyLength = 8)
    {
        var bytes = CreateBytes(messageId, bodyLength);
        var frame = PooledBufferWriter.Rent(bytes.Length);
        bytes.CopyTo(frame.GetSpan(bytes.Length));
        frame.Advance(bytes.Length);
        return frame;
    }

    public static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);
}

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Protocol;
using DotBoxD.Transports.Tcp;

namespace DotBoxD.Services.Tests.Transport.Receive.TcpPooling;

internal sealed class TcpReceiveTestPair : IAsyncDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private TcpReceiveTestPair(TcpConnection connection, TcpClient receiver, TcpClient peer)
    {
        Connection = connection;
        Receiver = receiver;
        Peer = peer;
    }

    public TcpConnection Connection { get; }

    public TcpClient Peer { get; }

    public TcpClient Receiver { get; }

    public static async Task<TcpReceiveTestPair> CreateAsync(TimeSpan? idleTimeout = null)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var peer = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            var accepting = listener.AcceptTcpClientAsync();
            await peer.ConnectAsync(endpoint.Address, endpoint.Port).WaitAsync(Guard);
            var receiver = await accepting.WaitAsync(Guard);
            receiver.NoDelay = true;
            return new TcpReceiveTestPair(
                new TcpConnection(receiver, idleTimeout ?? Timeout.InfiniteTimeSpan),
                receiver,
                peer);
        }
        catch
        {
            peer.Dispose();
            throw;
        }
    }

    public static byte[] CreateFrame(int messageId, int bodyLength = 8)
    {
        var body = new byte[bodyLength];
        for (var index = 0; index < body.Length; index++)
        {
            body[index] = unchecked((byte)(messageId + index));
        }

        using var frame = MessageFramer.FrameToPayload(messageId, MessageType.Response, body);
        return frame.Memory.ToArray();
    }

    public async Task QueueBytesAsync(ReadOnlyMemory<byte> bytes) =>
        await Peer.GetStream().WriteAsync(bytes).AsTask().WaitAsync(Guard);

    public async Task WaitForQueuedBytesAsync(int minimum)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (Receiver.Available < minimum)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("TCP bytes did not reach the receiver.");
            }

            await Task.Yield();
        }
    }

    public async Task WaitForPrefixAsync(int expectedFrameLength)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (BinaryPrimitives.ReadInt32LittleEndian(
                   Connection.FrameReceiveLengthBuffer) != expectedFrameLength)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The TCP receive did not consume the frame prefix.");
            }

            await Task.Yield();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        Peer.Dispose();
    }
}

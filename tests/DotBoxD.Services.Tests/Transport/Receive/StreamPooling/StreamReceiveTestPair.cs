using System.IO.Pipes;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Tests.Transport.Receive.StreamPooling;

internal sealed class StreamReceiveTestPair : IAsyncDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private StreamReceiveTestPair(
        StreamConnection connection,
        NamedPipeServerStream receiver,
        NamedPipeClientStream peer)
    {
        Connection = connection;
        Receiver = receiver;
        Peer = peer;
    }

    public StreamConnection Connection { get; }

    public NamedPipeClientStream Peer { get; }

    public NamedPipeServerStream Receiver { get; }

    public static async Task<StreamReceiveTestPair> CreateAsync(TimeSpan? idleTimeout = null)
    {
        var pipeName = $"dotboxd-stream-receive-{Guid.NewGuid():N}";
        var receiver = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var peer = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            var accepting = receiver.WaitForConnectionAsync();
            await peer.ConnectAsync().WaitAsync(Guard);
            await accepting.WaitAsync(Guard);
            return new StreamReceiveTestPair(
                new StreamConnection(
                    receiver,
                    ownsStream: true,
                    frameReadIdleTimeout: idleTimeout ?? Timeout.InfiniteTimeSpan),
                receiver,
                peer);
        }
        catch
        {
            receiver.Dispose();
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
        await Peer.WriteAsync(bytes).AsTask().WaitAsync(Guard);

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        Peer.Dispose();
    }
}

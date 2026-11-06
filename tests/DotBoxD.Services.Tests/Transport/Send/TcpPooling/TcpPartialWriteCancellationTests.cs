using System.Net.Sockets;
using DotBoxD.Transports.Tcp;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.TcpPooling;

// Frames are atomic. Cancelling one call after its frame started reaching the socket must neither truncate
// that frame nor tear down the shared connection (which would fail every other call on the peer): the
// frame completes intact once the receiver drains, and the connection keeps carrying later frames. A
// genuine I/O failure mid-write still poisons the connection so no frame follows a truncated one.
[Collection(TcpFrameSendOperationCollection.Name)]
public sealed class TcpPartialWriteCancellationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private const int LargeBodyLength = 12 * 1024 * 1024;

    [Theory]
    [InlineData((int)SendPath.Raw)]
    [InlineData((int)SendPath.Owned)]
    [InlineData((int)SendPath.RawFallback)]
    public async Task CancelledWriteMidFrame_CompletesFrameAndKeepsConnectionUsable(int pathValue)
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var large = TcpSendTestFrames.CreateBytes(messageId: 731, bodyLength: LargeBodyLength);
        using var cancellation = new CancellationTokenSource();

        // The receiver does not read, so the socket buffers eventually fill and a write stalls mid-frame.
        // Some platforms (Windows loopback) buffer a whole large frame, so keep sending until one stalls.
        var (send, completedBytes) = await SendUntilWriteStallsAsync(
            pair,
            large,
            cancellation.Token,
            (SendPath)pathValue);

        cancellation.Cancel();
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(send.IsCompleted, $"Send completed as {send.Status} after mid-write cancellation.");
        Assert.True(pair.Connection.IsConnected);

        // Draining the receiver lets the cancelled call's frame finish intact.
        await DiscardAsync(pair, completedBytes);
        var delivered = await pair.ReadAsync(large.Length).WaitAsync(Guard);
        await send.WaitAsync(Guard);
        Assert.True(large.AsSpan().SequenceEqual(delivered), "The stalled frame arrived corrupted.");

        Assert.True(pair.Connection.IsConnected);
        var next = TcpSendTestFrames.CreateBytes(messageId: 732);
        await pair.Connection.SendValueAsync(next).AsTask().WaitAsync(Guard);
        Assert.Equal(next, await pair.ReadAsync(next.Length));
    }

    [Fact]
    public async Task IoFailureMidFrame_PoisonsConnection()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var large = TcpSendTestFrames.CreateBytes(messageId: 733, bodyLength: LargeBodyLength);
        var (send, _) = await SendUntilWriteStallsAsync(
            pair,
            large,
            CancellationToken.None,
            SendPath.Raw);

        // Resetting the peer fails the stalled write with a genuine I/O error.
        pair.Peer.Client.LingerState = new LingerOption(enable: true, seconds: 0);
        pair.Peer.Client.Close();

        await Assert.ThrowsAnyAsync<IOException>(() => send.WaitAsync(Guard));
        Assert.False(pair.Connection.IsConnected);
        var next = TcpSendTestFrames.CreateBytes(messageId: 734);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => pair.Connection.SendValueAsync(next).AsTask().WaitAsync(Guard));
    }

    private static async Task<(Task Send, long CompletedBytes)> SendUntilWriteStallsAsync(
        TcpSendTestPair pair,
        byte[] frame,
        CancellationToken cancellationToken,
        SendPath path)
    {
        long completedBytes = 0;
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var send = Send(pair.Connection, frame, cancellationToken, path);
            if (await Task.WhenAny(send, Task.Delay(TimeSpan.FromMilliseconds(100))) != send)
            {
                return (send, completedBytes);
            }

            await send;
            completedBytes += frame.Length;
        }

        throw new InvalidOperationException("The non-reading peer never applied socket backpressure.");
    }

    private static Task Send(
        TcpConnection connection,
        byte[] frame,
        CancellationToken cancellationToken,
        SendPath path) => path switch
        {
            SendPath.Owned => connection.SendFrameValueAsync(
                TcpSendTestFrames.CreateOwned(frame),
                cancellationToken).AsTask(),
            SendPath.RawFallback => TcpFrameSendFallback.StartRawFromBeginning(
                connection,
                frame,
                cancellationToken).AsTask(),
            _ => connection.SendValueAsync(frame, cancellationToken).AsTask(),
        };

    private static async Task DiscardAsync(TcpSendTestPair pair, long count)
    {
        var buffer = new byte[64 * 1024];
        using var timeout = new CancellationTokenSource(Guard);
        while (count > 0)
        {
            var read = await pair.Peer.GetStream().ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)),
                timeout.Token);
            if (read == 0)
            {
                throw new InvalidDataException($"The TCP peer closed with {count} bytes undelivered.");
            }

            count -= read;
        }
    }

    private enum SendPath
    {
        Raw,
        Owned,
        RawFallback,
    }
}

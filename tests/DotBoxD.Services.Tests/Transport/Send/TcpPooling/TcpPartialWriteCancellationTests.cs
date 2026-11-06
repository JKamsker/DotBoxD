using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.TcpPooling;

// Regression: cancelling a send after part of the frame reached the socket must not leave the connection
// reusable. Otherwise the gate is released, the connection stays IsConnected, and the next frame is
// appended after a truncated one, desynchronizing the receiver's framing.
[Collection(TcpFrameSendOperationCollection.Name)]
public sealed class TcpPartialWriteCancellationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task CancelledWriteMidFrame_PoisonsConnectionInsteadOfAllowingReuse()
    {
        await using var pair = await TcpSendTestPair.CreateAsync();
        var large = TcpSendTestFrames.CreateBytes(messageId: 731, bodyLength: 12 * 1024 * 1024);
        using var cancellation = new CancellationTokenSource();

        // The receiver does not read, so the socket buffers eventually fill and a write stalls mid-frame.
        // Some platforms (Windows loopback) buffer a whole large frame, so keep sending until one stalls.
        var (send, completedBytes) = await SendUntilWriteStallsAsync(pair, large, cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(Guard));

        Assert.False(pair.Connection.IsConnected);
        var next = TcpSendTestFrames.CreateBytes(messageId: 732);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => pair.Connection.SendValueAsync(next).AsTask().WaitAsync(Guard));

        // The receiver sees at most the cancelled frame (usually a truncated prefix; Windows may already
        // have buffered all of it) followed by the close; no later frame ever follows it.
        var received = await DrainUntilClosedAsync(pair);
        var attempted = completedBytes + large.Length;
        Assert.True(received <= attempted, $"Receiver observed {received} of {attempted} bytes.");
    }

    private static async Task<(Task Send, long CompletedBytes)> SendUntilWriteStallsAsync(
        TcpSendTestPair pair,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        long completedBytes = 0;
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var send = pair.Connection.SendValueAsync(frame, cancellationToken).AsTask();
            if (await Task.WhenAny(send, Task.Delay(TimeSpan.FromMilliseconds(100))) != send)
            {
                return (send, completedBytes);
            }

            await send;
            completedBytes += frame.Length;
        }

        throw new InvalidOperationException("The non-reading peer never applied socket backpressure.");
    }

    private static async Task<long> DrainUntilClosedAsync(TcpSendTestPair pair)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        using var timeout = new CancellationTokenSource(Guard);
        try
        {
            while (true)
            {
                var read = await pair.Peer.GetStream().ReadAsync(buffer, timeout.Token);
                if (read == 0)
                {
                    return total;
                }

                total += read;
            }
        }
        catch (IOException)
        {
            // A reset after the abort is an equally valid end of stream.
            return total;
        }
    }
}

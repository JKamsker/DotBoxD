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

        // The receiver does not read, so the socket buffers fill and the write stalls mid-frame.
        var send = pair.Connection.SendValueAsync(large, cancellation.Token).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(send.IsCompleted);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(Guard));

        Assert.False(pair.Connection.IsConnected);
        var next = TcpSendTestFrames.CreateBytes(messageId: 732);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => pair.Connection.SendValueAsync(next).AsTask().WaitAsync(Guard));

        // The receiver sees only a truncated prefix of the large frame followed by the close; no later
        // frame ever follows the partial one.
        var received = await DrainUntilClosedAsync(pair);
        Assert.True(received < large.Length, $"Receiver observed {received} of {large.Length} bytes.");
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

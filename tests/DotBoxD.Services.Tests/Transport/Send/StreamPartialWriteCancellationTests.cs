using DotBoxD.Services.Tests.Transport.Send.StreamPooling;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

// Regression: once a stream write has started, a cancelled or failed write may have put part of the frame
// on the stream (named pipes). The connection must be poisoned rather than released for reuse, otherwise
// the next frame is appended after a truncated one and the receiver's framing desynchronizes.
[Collection(StreamSendOperationCollection.Name)]
public sealed class StreamPartialWriteCancellationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RawSend_CancelledMidWrite_PoisonsConnection()
    {
        var stream = new ControlledSendStream(SendBlockPoint.Write);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        using var frame = StreamSendTestFrames.Create(messageId: 741, out var bytes);
        using var cancellation = new CancellationTokenSource();

        var send = connection.SendValueAsync(bytes, cancellation.Token).AsTask();
        await stream.WriteEntered.WaitAsync(Guard);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(Guard));
        await AssertPoisonedAsync(connection, messageId: 742);
    }

    [Fact]
    public async Task OwnedFrameSend_CancelledMidWrite_PoisonsConnection()
    {
        var stream = new ControlledSendStream(SendBlockPoint.Write);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = StreamSendTestFrames.Create(messageId: 743, out _);
        using var cancellation = new CancellationTokenSource();

        var send = connection.SendFrameValueAsync(frame, cancellation.Token).AsTask();
        await stream.WriteEntered.WaitAsync(Guard);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(Guard));
        StreamSendTestFrames.AssertDisposed(frame);
        await AssertPoisonedAsync(connection, messageId: 744);
    }

    [Fact]
    public async Task OwnedFrameSend_CancelledBeforeWrite_KeepsConnectionUsable()
    {
        var stream = new ControlledSendStream();
        await using var connection = new StreamConnection(stream, ownsStream: false);
        Assert.True(connection.SendGate.Wait(0));
        var frame = StreamSendTestFrames.Create(messageId: 745, out _);
        using var cancellation = new CancellationTokenSource();

        // Cancelled while waiting for the gate: nothing reached the stream, so the connection stays usable.
        var send = connection.SendFrameValueAsync(frame, cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(Guard));
        connection.ReleaseSendGate();

        Assert.True(connection.IsConnected);
        using var next = StreamSendTestFrames.Create(messageId: 746, out var nextBytes);
        await connection.SendValueAsync(nextBytes).AsTask().WaitAsync(Guard);
        Assert.Equal(nextBytes, stream.WrittenBytes);
    }

    private static async Task AssertPoisonedAsync(StreamConnection connection, int messageId)
    {
        Assert.False(connection.IsConnected);
        using var next = StreamSendTestFrames.Create(messageId, out var nextBytes);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => connection.SendValueAsync(nextBytes).AsTask().WaitAsync(Guard));
    }
}

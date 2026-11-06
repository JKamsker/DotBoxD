using DotBoxD.Services.Tests.Transport.Send.StreamPooling;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

// Frames are atomic. The caller's token covers only waiting for the send gate: once a write has started,
// cancelling that one call must neither truncate the frame nor tear down the shared connection (which
// would fail every other call on the peer). A genuine I/O failure mid-write still poisons the connection,
// so no later frame is ever appended after a truncated one.
[Collection(StreamSendOperationCollection.Name)]
public sealed class StreamPartialWriteCancellationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task RawSend_CancelledMidWrite_CompletesFrameAndKeepsConnectionUsable()
    {
        var stream = new ControlledSendStream(SendBlockPoint.Write);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        using var frame = StreamSendTestFrames.Create(messageId: 741, out var bytes);
        using var cancellation = new CancellationTokenSource();

        var send = connection.SendValueAsync(bytes, cancellation.Token).AsTask();
        await stream.WriteEntered.WaitAsync(Guard);
        cancellation.Cancel();
        await AssertStillWritingAsync(send);

        stream.ReleaseWrite();
        await send.WaitAsync(Guard);

        Assert.Equal(bytes, stream.WrittenBytes);
        await AssertUsableAsync(connection, stream, messageId: 742);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedFrameSend_CancelledAfterWriteStarted_CompletesFrameAndKeepsConnectionUsable(
        bool cancelDuringFlush)
    {
        var stream = new ControlledSendStream(
            cancelDuringFlush ? SendBlockPoint.Flush : SendBlockPoint.Write);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = StreamSendTestFrames.Create(messageId: 743, out var bytes);
        using var cancellation = new CancellationTokenSource();

        var send = connection.SendFrameValueAsync(frame, cancellation.Token).AsTask();
        var entered = cancelDuringFlush ? stream.FlushEntered : stream.WriteEntered;
        await entered.WaitAsync(Guard);
        cancellation.Cancel();
        await AssertStillWritingAsync(send);

        stream.ReleaseWrite();
        stream.ReleaseFlush();
        await send.WaitAsync(Guard);

        StreamSendTestFrames.AssertDisposed(frame);
        Assert.Equal(bytes, stream.WrittenBytes);
        await AssertUsableAsync(connection, stream, messageId: 744);
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

        Assert.Null(stream.WrittenBytes);
        StreamSendTestFrames.AssertDisposed(frame);
        await AssertUsableAsync(connection, stream, messageId: 746);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IoFailureMidWrite_PoisonsConnection(bool ownedFrame)
    {
        var failure = new IOException("simulated mid-write failure");
        var stream = new ControlledSendStream(SendBlockPoint.Write, writeFailure: failure);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = StreamSendTestFrames.Create(messageId: 747, out var bytes);

        var send = ownedFrame
            ? connection.SendFrameValueAsync(frame).AsTask()
            : connection.SendValueAsync(bytes).AsTask();
        await stream.WriteEntered.WaitAsync(Guard);
        stream.ReleaseWrite();

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => send.WaitAsync(Guard)));
        if (!ownedFrame)
        {
            frame.Dispose();
        }

        Assert.False(connection.IsConnected);
        using var next = StreamSendTestFrames.Create(messageId: 748, out var nextBytes);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => connection.SendValueAsync(nextBytes).AsTask().WaitAsync(Guard));
    }

    private static async Task AssertStillWritingAsync(Task send)
    {
        // The caller's cancellation must not abandon a frame whose bytes may already be on the stream.
        await Task.Delay(SettleDelay);
        Assert.False(send.IsCompleted, $"Send completed as {send.Status} after mid-write cancellation.");
    }

    private static async Task AssertUsableAsync(
        StreamConnection connection,
        ControlledSendStream stream,
        int messageId)
    {
        Assert.True(connection.IsConnected);
        using var next = StreamSendTestFrames.Create(messageId, out var nextBytes);
        stream.ReleaseWrite();
        stream.ReleaseFlush();
        await connection.SendValueAsync(nextBytes).AsTask().WaitAsync(Guard);
        Assert.Equal(nextBytes, stream.WrittenBytes);
    }
}

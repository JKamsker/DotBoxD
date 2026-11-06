using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.StreamPooling;

[Collection(StreamSendOperationCollection.Name)]
public sealed class StreamFrameSendFailureTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingFailure_ReleasesGateAndFrame(bool failWrite)
    {
        var stage = failWrite
            ? PendingStreamSendStage.Write
            : PendingStreamSendStage.Flush;
        await using var stream = new ControlledPendingSendStream(stage);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = StreamSendTestFrames.Create(40 + (int)stage, out _);
        var marker = new IOException("controlled stream failure");
        var send = connection.SendFrameValueAsync(frame);

        if (stage == PendingStreamSendStage.Write)
        {
            await stream.WriteEntered.WaitAsync(Guard);
            stream.CancelOrFailWrite(marker);
        }
        else
        {
            await stream.FlushEntered.WaitAsync(Guard);
            stream.FailFlush(marker);
        }

        var thrown = await Assert.ThrowsAsync<IOException>(
            () => send.AsTask().WaitAsync(Guard));
        Assert.Same(marker, thrown);
        StreamSendTestFrames.AssertDisposed(frame);
        Assert.Equal(1, connection.SendGate.CurrentCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PendingCancellation_IsNotForwardedAndFrameCompletes(
        bool cancelWrite)
    {
        var stage = cancelWrite
            ? PendingStreamSendStage.Write
            : PendingStreamSendStage.Flush;
        await using var stream = new ControlledPendingSendStream(stage);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        using var cancellation = new CancellationTokenSource();
        var frame = StreamSendTestFrames.Create(50 + (int)stage, out var expected);
        var send = connection.SendFrameValueAsync(frame, cancellation.Token);
        var entered = stage == PendingStreamSendStage.Write
            ? stream.WriteEntered
            : stream.FlushEntered;
        await entered.WaitAsync(Guard);

        // Frames are atomic: cancelling after the write started must not truncate the frame or
        // poison the shared connection, so the token is never forwarded to the stream.
        cancellation.Cancel();
        Assert.False(send.IsCompleted);
        Assert.False(stream.WriteToken.CanBeCanceled);
        if (stage == PendingStreamSendStage.Write)
        {
            stream.CompleteWrite();
        }
        else
        {
            Assert.False(stream.FlushToken.CanBeCanceled);
            stream.CompleteFlush();
        }

        await send.AsTask().WaitAsync(Guard);
        Assert.Equal(expected, stream.WrittenBytes);
        StreamSendTestFrames.AssertDisposed(frame);
        Assert.Equal(1, connection.SendGate.CurrentCount);
        Assert.True(connection.IsConnected);
    }

    [Fact]
    public async Task DisposeWhileGatePending_FaultsAndLeavesOneTerminalPermit()
    {
        await using var stream = new ControlledPendingSendStream(PendingStreamSendStage.None);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        Assert.True(connection.SendGate.Wait(0));
        var frame = StreamSendTestFrames.Create(60, out _);
        var send = connection.SendFrameValueAsync(frame);
        Assert.False(send.IsCompleted);

        try
        {
            await connection.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => send.AsTask().WaitAsync(Guard));
            StreamSendTestFrames.AssertDisposed(frame);
            Assert.Equal(1, connection.SendGate.CurrentCount);
        }
        finally
        {
            connection.ReleaseSendGate();
            Assert.Equal(1, connection.SendGate.CurrentCount);
        }
    }
}

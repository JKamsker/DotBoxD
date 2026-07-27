using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class OwnedFrameSendLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Synchronous_send_disposes_frame_before_return()
    {
        await using var stream = new ControlledSendStream();
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = CreateFrame(out var expected);

        var send = connection.SendFrameValueAsync(frame);

        Assert.True(send.IsCompletedSuccessfully);
        AssertDisposed(frame);
        await send;
        Assert.Equal(expected, stream.WrittenBytes);
    }

    [Fact]
    public async Task Pending_write_releases_frame_when_caller_does_not_await_send()
    {
        await using var stream = new ControlledSendStream(SendBlockPoint.Write);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = CreateFrame(out var expected);

        var send = connection.SendFrameValueAsync(frame);
        var completion = send.AsTask();
        await stream.WriteEntered.WaitAsync(Timeout);
        Assert.Equal(expected, frame.WrittenMemory.ToArray());

        stream.ReleaseWrite();
        var winner = await Task.WhenAny(completion, Task.Delay(Timeout));

        Assert.Same(completion, winner);
        AssertDisposed(frame);
        await completion;
        Assert.Equal(expected, stream.WrittenBytes);
    }

    [Fact]
    public async Task Pending_flush_keeps_frame_until_flush_completes()
    {
        await using var stream = new ControlledSendStream(SendBlockPoint.Flush);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = CreateFrame(out var expected);

        var send = connection.SendFrameValueAsync(frame);
        await stream.FlushEntered.WaitAsync(Timeout);
        Assert.Equal(expected, frame.WrittenMemory.ToArray());

        stream.ReleaseFlush();
        await send.AsTask().WaitAsync(Timeout);

        AssertDisposed(frame);
        Assert.Equal(expected, stream.WrittenBytes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Write_or_flush_failure_disposes_frame(bool failWrite)
    {
        var failure = new IOException("simulated send failure");
        await using var stream = new ControlledSendStream(
            writeFailure: failWrite ? failure : null,
            flushFailure: failWrite ? null : failure);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = CreateFrame(out _);

        var thrown = await Assert.ThrowsAsync<IOException>(
            () => connection.SendFrameValueAsync(frame).AsTask());

        Assert.Same(failure, thrown);
        AssertDisposed(frame);

        var followUp = CreateFrame(out var expected);
        await connection.SendFrameValueAsync(followUp).AsTask().WaitAsync(Timeout);
        AssertDisposed(followUp);
        Assert.Equal(expected, stream.WrittenBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_write_or_flush_disposes_frame(bool cancelDuringFlush)
    {
        var blockPoint = cancelDuringFlush ? SendBlockPoint.Flush : SendBlockPoint.Write;
        await using var stream = new ControlledSendStream(blockPoint);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        using var cancellation = new CancellationTokenSource();
        var frame = CreateFrame(out _);
        var send = connection.SendFrameValueAsync(frame, cancellation.Token).AsTask();
        var entered = cancelDuringFlush ? stream.FlushEntered : stream.WriteEntered;
        await entered.WaitAsync(Timeout);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(Timeout));
        AssertDisposed(frame);
    }

    private static PooledBufferWriter CreateFrame(out byte[] expected)
    {
        var frame = new PooledBufferWriter(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 42, MessageType.Request, ReadOnlySpan<byte>.Empty);
        expected = frame.WrittenMemory.ToArray();
        return frame;
    }

    private static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);
}

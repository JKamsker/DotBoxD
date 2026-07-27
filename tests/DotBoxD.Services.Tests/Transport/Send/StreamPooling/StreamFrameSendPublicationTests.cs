using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.StreamPooling;

[Collection(StreamSendOperationCollection.Name)]
public sealed class StreamFrameSendPublicationTests
{
    [Fact]
    public async Task InlineThrowingConsumer_SeesCleanupAndCanReenterConnection()
    {
        await using var stream = new ControlledPendingSendStream(PendingStreamSendStage.Write);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = StreamSendTestFrames.Create(70, out var expected);
        var send = connection.SendFrameValueAsync(frame);
        var marker = new InlineConsumerException();
        var observed = new InlineObservation();
#pragma warning disable xUnit1030 // This regression intentionally pins inline source publication.
        var awaiter = send.ConfigureAwait(false).GetAwaiter();
#pragma warning restore xUnit1030
        awaiter.UnsafeOnCompleted(() =>
        {
            observed.FrameWasDisposed = Record.Exception(
                () => _ = frame.WrittenMemory) is ObjectDisposedException;
            observed.GateWasReleased = connection.SendGate.Wait(0);
            if (observed.GateWasReleased)
            {
                connection.SendGate.Release();
            }

#pragma warning disable xUnit1031 // Both ValueTasks are proven complete in this raw continuation.
            awaiter.GetResult();
            var reentrant = connection.SendValueAsync(expected);
            observed.ReentrantSendCompleted = reentrant.IsCompletedSuccessfully;
            reentrant.GetAwaiter().GetResult();
#pragma warning restore xUnit1031
            throw marker;
        });

        var thrown = Assert.Throws<InlineConsumerException>(stream.CompleteWrite);

        Assert.Same(marker, thrown);
        Assert.True(observed.FrameWasDisposed);
        Assert.True(observed.GateWasReleased);
        Assert.True(observed.ReentrantSendCompleted);
        Assert.Equal(1, connection.SendGate.CurrentCount);
        Assert.Equal(2, stream.WriteCount);
    }

    [Fact]
    public async Task ConsumingCompletionAfterWriterReuse_DoesNotDisposeNewLease()
    {
        await using var stream = new ControlledPendingSendStream(PendingStreamSendStage.Write);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = StreamSendTestFrames.Rent(80, out var expected);
        var oldLease = frame.LeaseToken;
        var send = connection.SendFrameValueAsync(frame);
        Assert.False(send.IsCompleted);
        Assert.Equal(expected, frame.GetWrittenMemory(oldLease).ToArray());

        stream.CompleteWrite();

        Assert.True(send.IsCompletedSuccessfully);
        Assert.Throws<ObjectDisposedException>(() => frame.GetWrittenMemory(oldLease));
        var replacement = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
        try
        {
            Assert.Same(frame, replacement);
            MessageFramer.WriteFrame(
                replacement,
                81,
                MessageType.Request,
                ReadOnlySpan<byte>.Empty);
            var replacementLease = replacement.LeaseToken;

            await send;

            Assert.Equal(
                MessageFramer.HeaderSize,
                replacement.GetWrittenMemory(replacementLease).Length);
        }
        finally
        {
            replacement.Dispose();
        }
    }

    private sealed class InlineObservation
    {
        public bool FrameWasDisposed { get; set; }
        public bool GateWasReleased { get; set; }
        public bool ReentrantSendCompleted { get; set; }
    }

    private sealed class InlineConsumerException : Exception;
}

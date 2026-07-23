using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.StreamPooling;

[Collection(StreamSendOperationCollection.Name)]
public sealed class StreamFrameSendFallbackTests
{
    private static readonly AsyncLocal<string?> Context = new();
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task SaturatedPopulation_FallbackCancelsAndSucceedsThenPoolRecovers()
    {
        var capacity = BoundedTransportOperationPool<object>.MaxRetainedCount;
        var holders = Enumerable.Range(0, capacity)
            .Select(index => SendHolder.Start(100 + index))
            .ToArray();
        try
        {
            Assert.All(holders, static holder => Assert.False(holder.Send.IsCompleted));
            Assert.Equal(0, StreamFrameSendOperation.RetainedCount);

            await VerifyCanceledFallbackAsync();
            Assert.Equal(0, StreamFrameSendOperation.RetainedCount);
            await VerifySuccessfulFallbackAsync();
            Assert.Equal(0, StreamFrameSendOperation.RetainedCount);

            foreach (var holder in holders)
            {
                holder.Stream.CompleteWrite();
            }

            foreach (var holder in holders)
            {
                await holder.ConsumeAsync();
                StreamSendTestFrames.AssertDisposed(holder.Frame);
            }

            Assert.Equal(capacity, StreamFrameSendOperation.RetainedCount);
            await VerifyPoolRecoveryAsync(capacity);
        }
        finally
        {
            await ReleaseAndDisposeAsync(holders);
        }
    }

    private static async Task VerifyCanceledFallbackAsync()
    {
        await using var stream = new ControlledPendingSendStream(PendingStreamSendStage.Write);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        using var cancellation = new CancellationTokenSource();
        var frame = StreamSendTestFrames.Create(200, out _);
        var send = connection.SendFrameValueAsync(frame, cancellation.Token);
        Assert.False(send.IsCompleted);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => send.AsTask().WaitAsync(Guard));
        StreamSendTestFrames.AssertDisposed(frame);
        Assert.Equal(1, connection.SendGate.CurrentCount);
    }

    private static async Task VerifySuccessfulFallbackAsync()
    {
        var previous = Context.Value;
        Context.Value = "caller";
        try
        {
            await using var stream = new ControlledPendingSendStream(
                PendingStreamSendStage.Write | PendingStreamSendStage.Flush)
            {
                ObserveContext = static () => Context.Value,
            };
            await using var connection = new StreamConnection(stream, ownsStream: false);
            var frame = StreamSendTestFrames.Create(201, out _);
            var send = connection.SendFrameValueAsync(frame);

            await Task.Run(() =>
            {
                Context.Value = "producer";
                stream.CompleteWrite();
            });
            await stream.FlushEntered.WaitAsync(Guard);
            Assert.Equal("caller", stream.FlushContext);
            stream.CompleteFlush();
            await send.AsTask().WaitAsync(Guard);

            StreamSendTestFrames.AssertDisposed(frame);
            Assert.Equal(1, connection.SendGate.CurrentCount);
        }
        finally
        {
            Context.Value = previous;
        }
    }

    private static async Task VerifyPoolRecoveryAsync(int capacity)
    {
        await using var stream = new ControlledPendingSendStream(PendingStreamSendStage.Write);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = StreamSendTestFrames.Create(202, out _);
        var send = connection.SendFrameValueAsync(frame);
        Assert.False(send.IsCompleted);
        Assert.Equal(capacity - 1, StreamFrameSendOperation.RetainedCount);

        stream.CompleteWrite();
        await send.AsTask().WaitAsync(Guard);

        StreamSendTestFrames.AssertDisposed(frame);
        Assert.Equal(capacity, StreamFrameSendOperation.RetainedCount);
    }

    private static async Task ReleaseAndDisposeAsync(IEnumerable<SendHolder> holders)
    {
        foreach (var holder in holders)
        {
            if (holder.IsActive)
            {
                try
                {
                    holder.Stream.CompleteWrite();
                    await holder.ConsumeAsync();
                }
                catch
                {
                    // Preserve the primary assertion while releasing pending ownership.
                }
            }

            await holder.Connection.DisposeAsync();
            await holder.Stream.DisposeAsync();
        }
    }

    private sealed class SendHolder
    {
        private SendHolder(
            ControlledPendingSendStream stream,
            StreamConnection connection,
            PooledBufferWriter frame,
            ValueTask send)
        {
            Stream = stream;
            Connection = connection;
            Frame = frame;
            Send = send;
        }

        public StreamConnection Connection { get; }
        public PooledBufferWriter Frame { get; }
        public bool IsActive { get; private set; } = true;
        public ValueTask Send { get; }
        public ControlledPendingSendStream Stream { get; }

        public async Task ConsumeAsync()
        {
            if (!IsActive)
            {
                throw new InvalidOperationException("The holder send was already consumed.");
            }

            try
            {
                await Send.AsTask().WaitAsync(Guard);
            }
            finally
            {
                IsActive = false;
            }
        }

        public static SendHolder Start(int messageId)
        {
            var stream = new ControlledPendingSendStream(PendingStreamSendStage.Write);
            var connection = new StreamConnection(stream, ownsStream: false);
            var frame = StreamSendTestFrames.Create(messageId, out _);
            var send = connection.SendFrameValueAsync(frame);
            return new SendHolder(stream, connection, frame, send);
        }
    }
}

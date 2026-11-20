using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Tests.Transport.Send.StreamPooling;
using DotBoxD.Services.Transport;
using Shared;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

// Frames are atomic, so a started write ignores the caller's token. Closing the connection is then the
// only thing that can end a stalled write. For a borrowed stream (ownsStream: false) close does not
// dispose the stream, so it must cancel the started write itself; otherwise peer teardown, which waits
// for in-flight responses, hangs forever. The close-cancelled write must still fail as a lost connection
// (the same ObjectDisposedException as a send after close), never as a cancellation the caller never
// requested: retry policies and RPC callers treat cancellation as deliberate.
[Collection(StreamSendOperationCollection.Name)]
public sealed class StreamConnectionStalledWriteCloseTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Close_FailsStalledWriteAsConnectionLossAndPoisonsConnection(
        bool ownedFrame,
        bool stallFlush,
        bool ownsStream)
    {
        var stream = new ControlledSendStream(stallFlush ? SendBlockPoint.Flush : SendBlockPoint.Write);
        var connection = new StreamConnection(stream, ownsStream: ownsStream);
        var frame = StreamSendTestFrames.Create(messageId: 761, out var bytes);
        var send = ownedFrame
            ? connection.SendFrameValueAsync(frame).AsTask()
            : connection.SendValueAsync(bytes).AsTask();
        try
        {
            var entered = stallFlush ? stream.FlushEntered : stream.WriteEntered;
            await entered.WaitAsync(Guard);

            await connection.DisposeAsync().AsTask().WaitAsync(Guard);

            await AssertConnectionClosedAsync(send);
            Assert.False(connection.IsConnected);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => connection.SendValueAsync(bytes).AsTask().WaitAsync(Guard));
            if (ownedFrame)
            {
                StreamSendTestFrames.AssertDisposed(frame);
            }
        }
        finally
        {
            stream.ReleaseWrite();
            stream.ReleaseFlush();
            await Record.ExceptionAsync(() => send.WaitAsync(Guard));
            frame.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseRacingWriteStart_FailsSynchronousWriteAsConnectionLoss(bool ownedFrame)
    {
        // The sender passed its disposed check, then close cancelled the lifetime token before the stream
        // looked at it, so the write completes synchronously as cancelled.
        await using var stream = new CloseOnWriteStream();
        var connection = new StreamConnection(stream, ownsStream: false);
        stream.Connection = connection;
        var frame = StreamSendTestFrames.Create(messageId: 762, out var bytes);
        try
        {
            var send = ownedFrame
                ? connection.SendFrameValueAsync(frame).AsTask()
                : connection.SendValueAsync(bytes).AsTask();

            await AssertConnectionClosedAsync(send);
            Assert.False(connection.IsConnected);
            if (ownedFrame)
            {
                StreamSendTestFrames.AssertDisposed(frame);
            }
        }
        finally
        {
            frame.Dispose();
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task Close_FailsStalledFallbackWriteAsConnectionLoss()
    {
        // Exhaust the reusable send sources so the owned-frame send takes the Task-backed fallback.
        var capacity = BoundedTransportOperationPool<object>.MaxRetainedCount;
        var holders = Enumerable.Range(0, capacity)
            .Select(static index => PendingHolder.Start(800 + index))
            .ToArray();
        var stream = new ControlledSendStream(SendBlockPoint.Write);
        var connection = new StreamConnection(stream, ownsStream: true);
        var frame = StreamSendTestFrames.Create(messageId: 763, out _);
        Task? send = null;
        try
        {
            Assert.Equal(0, StreamFrameSendOperation.RetainedCount);
            send = connection.SendFrameValueAsync(frame).AsTask();
            await stream.WriteEntered.WaitAsync(Guard);
            Assert.Equal(0, StreamFrameSendOperation.RetainedCount);

            await connection.DisposeAsync().AsTask().WaitAsync(Guard);

            await AssertConnectionClosedAsync(send);
            Assert.False(connection.IsConnected);
            StreamSendTestFrames.AssertDisposed(frame);
        }
        finally
        {
            stream.ReleaseWrite();
            if (send is not null)
            {
                await Record.ExceptionAsync(() => send.WaitAsync(Guard));
            }

            frame.Dispose();
            foreach (var holder in holders)
            {
                await holder.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task PeerDispose_CompletesWhenResponseWriteStallsOnBorrowedStream()
    {
        var (serverStream, clientStream) = StalledWriteDuplexStream.CreatePair();
        var serverConnection = new StreamConnection(serverStream, ownsStream: false);
        var server = RpcPeer.Over(serverConnection, new MessagePackRpcSerializer())
            .Provide<IGameService>(new TestGameService())
            .Start();
        var client = RpcPeer.Over(
                new StreamConnection(clientStream),
                new MessagePackRpcSerializer(),
                new RpcPeerOptions { RejectInboundCalls = true })
            .Start();
        Task? call = null;
        try
        {
            call = client.GetGameService().GetServerStatusAsync();
            await serverStream.WriteEntered.WaitAsync(Guard);

            // The response write is stalled on the borrowed stream; teardown must still complete.
            var dispose = server.DisposeAsync().AsTask();
            var completed = await Task.WhenAny(dispose, Task.Delay(Guard));
            Assert.Same(dispose, completed);
            await dispose;
        }
        finally
        {
            serverStream.ReleaseWrites();
            await Record.ExceptionAsync(() => server.DisposeAsync().AsTask().WaitAsync(Guard));
            await client.DisposeAsync();
            if (call is not null)
            {
                await Record.ExceptionAsync(() => call.WaitAsync(Guard));
            }

            await serverStream.DisposeAsync();
        }
    }

    private static async Task AssertConnectionClosedAsync(Task send)
    {
        var error = await Record.ExceptionAsync(() => send.WaitAsync(Guard));
        Assert.NotNull(error);
        Assert.False(send.IsCanceled, "A close-ended write must fault, not report caller cancellation.");
        var disposed = Assert.IsType<ObjectDisposedException>(error);
        Assert.Equal(nameof(StreamConnection), disposed.ObjectName);
    }

    private sealed class CloseOnWriteStream : ControlledSendStream
    {
        public StreamConnection? Connection { get; set; }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Connection!.DisposeAsync().AsTask().WaitAsync(Guard).GetAwaiter().GetResult();
            return ValueTask.FromCanceled(cancellationToken);
        }
    }

    private sealed class PendingHolder : IAsyncDisposable
    {
        private readonly ControlledPendingSendStream _stream;
        private readonly StreamConnection _connection;
        private readonly PooledBufferWriter _frame;
        private readonly ValueTask _send;

        private PendingHolder(int messageId)
        {
            _stream = new ControlledPendingSendStream(PendingStreamSendStage.Write);
            _connection = new StreamConnection(_stream, ownsStream: false);
            _frame = StreamSendTestFrames.Create(messageId, out _);
            _send = _connection.SendFrameValueAsync(_frame);
        }

        public static PendingHolder Start(int messageId) => new(messageId);

        public async ValueTask DisposeAsync()
        {
            _stream.CompleteWrite();
            await Record.ExceptionAsync(() => _send.AsTask().WaitAsync(Guard));
            _frame.Dispose();
            await _connection.DisposeAsync();
            await _stream.DisposeAsync();
        }
    }
}

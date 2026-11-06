using DotBoxD.Codecs.MessagePack;
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
// for in-flight responses, hangs forever.
[Collection(StreamSendOperationCollection.Name)]
public sealed class StreamConnectionStalledWriteCloseTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Close_FailsStalledWriteOnBorrowedStreamAndPoisonsConnection(
        bool ownedFrame,
        bool stallFlush)
    {
        var stream = new ControlledSendStream(stallFlush ? SendBlockPoint.Flush : SendBlockPoint.Write);
        var connection = new StreamConnection(stream, ownsStream: false);
        var frame = StreamSendTestFrames.Create(messageId: 761, out var bytes);
        var send = ownedFrame
            ? connection.SendFrameValueAsync(frame).AsTask()
            : connection.SendValueAsync(bytes).AsTask();
        try
        {
            var entered = stallFlush ? stream.FlushEntered : stream.WriteEntered;
            await entered.WaitAsync(Guard);

            await connection.DisposeAsync().AsTask().WaitAsync(Guard);

            var error = await Record.ExceptionAsync(() => send.WaitAsync(Guard));
            Assert.NotNull(error);
            Assert.IsNotType<TimeoutException>(error);
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
}

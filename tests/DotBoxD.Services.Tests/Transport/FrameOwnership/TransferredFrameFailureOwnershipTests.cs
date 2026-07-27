using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Tests.Protocol.Buffers;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.FrameOwnership;

[Collection(PooledBufferWriterCacheCollection.Name)]
public sealed class TransferredFrameFailureOwnershipTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Peer_sender_failure_does_not_dispose_a_reused_writer_lease()
    {
        var channel = new DisposeThenFailFrameChannel();
        using var sender = new RpcPeerSender(channel, static () => false);
        var transferred = CreateFrame();
        var send = sender.SendFrameValueAsync(transferred, CancellationToken.None);
        var reused = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
        try
        {
            Assert.Same(transferred, reused);
            _ = reused.WrittenMemory;
            channel.Fail(new IOException("simulated peer send failure"));

            await Assert.ThrowsAsync<IOException>(() => send.AsTask().WaitAsync(Timeout));
            _ = reused.WrittenMemory;
        }
        finally
        {
            reused.Dispose();
        }
    }

    [Fact]
    public async Task Stream_item_failure_does_not_dispose_a_reused_writer_lease()
    {
        var serializer = new MessagePackRpcSerializer();
        var channel = new DisposeThenFailFrameChannel();
        var streams = new RpcStreamManager(
            serializer,
            channel.SendAsync,
            exceptionTransformer: null,
            channel.SendFrameValueAsync);
        var handle = streams.ReserveOutbound(RpcStreamKind.Items);
        await using var outbound = streams.RegisterOutbound(
            RpcStreamAttachment.FromAsyncEnumerable(handle, EmptyItems()),
            CancellationToken.None);
        using var credit = RpcRawFrame.FrameInt32(handle.StreamId, MessageType.StreamCredit, 1);
        Assert.True(streams.TryAddCredit(credit));

        PooledBufferWriter? reused = null;
        try
        {
            var send = streams.SendStreamItemAsync(
                handle.StreamId,
                42,
                serializer,
                CancellationToken.None);
            var transferred = Assert.IsType<PooledBufferWriter>(channel.TransferredFrame);
            reused = PooledBufferWriter.Rent(MessageFramer.HeaderSize);

            Assert.Same(transferred, reused);
            _ = reused.WrittenMemory;
            channel.Fail(new IOException("simulated stream send failure"));

            await Assert.ThrowsAsync<IOException>(() => send.WaitAsync(Timeout));
            _ = reused.WrittenMemory;
        }
        finally
        {
            reused?.Dispose();
            streams.Stop();
        }
    }

    private static PooledBufferWriter CreateFrame()
    {
        var frame = PooledBufferWriter.Rent(MessageFramer.HeaderSize);
        MessageFramer.WriteFrame(frame, 1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        return frame;
    }

    private static async IAsyncEnumerable<int> EmptyItems()
    {
        await Task.Yield();
        yield break;
    }

    private sealed class DisposeThenFailFrameChannel : IRpcFrameChannel
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected => true;
        public string RemoteEndpoint => "frame-failure://test";
        public PooledBufferWriter? TransferredFrame { get; private set; }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.CompletedTask;

        public ValueTask SendValueAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            default;

        public ValueTask SendFrameValueAsync(
            PooledBufferWriter frame,
            CancellationToken ct = default)
        {
            TransferredFrame = frame;
            frame.Dispose();
            return new ValueTask(_completion.Task);
        }

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            Task.FromResult(Payload.Empty);

        public ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default) =>
            new(Payload.Empty);

        public ValueTask<RpcFrame> ReceiveFrameValueAsync(CancellationToken ct = default) =>
            new(new RpcFrame(Payload.Empty));

        public ValueTask DisposeAsync() => default;

        public void Fail(Exception exception) => _completion.TrySetException(exception);
    }
}

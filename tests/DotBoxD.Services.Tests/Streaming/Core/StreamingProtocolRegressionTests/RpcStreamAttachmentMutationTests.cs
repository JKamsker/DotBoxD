using System.IO.Pipelines;
using System.Threading.Channels;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcStreamAttachmentMutationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void FromStream_NonPositiveHandle_ReportsStableArgumentMessage()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => RpcStreamAttachment.FromStream(
                new RpcStreamHandle(0, RpcStreamKind.Binary),
                Stream.Null));

        Assert.Equal("handle", ex.ParamName);
        Assert.Contains("Stream handle stream id must be positive.", ex.Message);
    }

    [Fact]
    public async Task StreamAttachment_PumpCoreAsync_SendsChunkAndDisposesOwnedStream()
    {
        var sentPayloads = new List<byte[]>();
        var serializer = new MessagePackRpcSerializer();
        var handle = new RpcStreamHandle(10, RpcStreamKind.Binary);
        var streams = new RpcStreamManager(serializer, SendAsync, exceptionTransformer: null);
        streams.ReserveOutbound(handle.StreamId);
        var source = new CountingReadStream(new byte[] { 1, 2, 3 });
        var attachment = RpcStreamAttachment.FromStream(handle, source, leaveOpen: false);
        await using var outbound = streams.RegisterOutbound(attachment, CancellationToken.None);
        using var credit = RpcRawFrame.FrameInt32(handle.StreamId, MessageType.StreamCredit, 1);
        Assert.True(streams.TryAddCredit(credit));

        await attachment.PumpCoreAsync(streams, serializer, CancellationToken.None)
            .WaitAsync(Timeout);

        var payload = Assert.Single(sentPayloads);
        Assert.Equal(new byte[] { 1, 2, 3 }, payload);
        Assert.Equal(1, source.DisposeCount);

        Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out var id, out var type));
            if (type == MessageType.StreamItem)
            {
                Assert.Equal(handle.StreamId, id);
                sentPayloads.Add(frame.Slice(MessageFramer.HeaderSize).ToArray());
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PipeAttachment_PumpCoreAsync_CompletesReaderWhenOwned()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var pipe = new Pipe();
        await pipe.Writer.CompleteAsync();
        var attachment = RpcStreamAttachment.FromPipe(
            new RpcStreamHandle(7, RpcStreamKind.Binary),
            pipe,
            completeReader: true);

        await attachment.PumpCoreAsync(streams, serializer, CancellationToken.None)
            .WaitAsync(Timeout);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipe.Reader.ReadAsync().AsTask().WaitAsync(Timeout));
    }

    [Fact]
    public async Task PipeAttachment_PumpCoreAsync_StopsWhenPendingReadIsCanceled()
    {
        var sentItems = 0;
        var serializer = new MessagePackRpcSerializer();
        var handle = new RpcStreamHandle(11, RpcStreamKind.Binary);
        var streams = new RpcStreamManager(serializer, SendAsync, exceptionTransformer: null);
        var pipe = new Pipe();
        var attachment = RpcStreamAttachment.FromPipe(handle, pipe, completeReader: true);
        streams.ReserveOutbound(handle.StreamId);
        await using var outbound = streams.RegisterOutbound(attachment, CancellationToken.None);
        using var credit = RpcRawFrame.FrameInt32(handle.StreamId, MessageType.StreamCredit, 1);
        Assert.True(streams.TryAddCredit(credit));

        var pumpTask = attachment.PumpCoreAsync(streams, serializer, CancellationToken.None);
        pipe.Reader.CancelPendingRead();

        try
        {
            await pumpTask.WaitAsync(Timeout);
        }
        catch
        {
            await pipe.Writer.CompleteAsync();
            await pumpTask.WaitAsync(Timeout);
            throw;
        }

        Assert.Equal(0, Volatile.Read(ref sentItems));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipe.Reader.ReadAsync().AsTask().WaitAsync(Timeout));
        await pipe.Writer.CompleteAsync();

        Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (MessageFramer.TryReadFrameHeader(frame, out _, out var type) &&
                type == MessageType.StreamItem)
            {
                Interlocked.Increment(ref sentItems);
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PipeAttachment_DisposeSourceOnce_LeavesReaderOpenWhenNotOwned()
    {
        var pipe = new Pipe();
        var attachment = RpcStreamAttachment.FromPipe(
            new RpcStreamHandle(8, RpcStreamKind.Binary),
            pipe,
            completeReader: false);

        await attachment.DisposeSourceOnceAsync();

        await pipe.Writer.CompleteAsync();
        var result = await pipe.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.True(result.IsCompleted);
        pipe.Reader.AdvanceTo(result.Buffer.End);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public void AttachmentSet_FromEmptyArray_ReturnsEmptyShape()
    {
        var set = RpcStreamAttachmentSet.FromArray(Array.Empty<RpcStreamAttachment>());

        Assert.True(set.IsEmpty);
        Assert.False(set.IsSingle);
        Assert.Throws<InvalidOperationException>(() => set.Many);
    }

    [Fact]
    public async Task RpcStreamChunk_Dispose_ReleasesCredit()
    {
        var credits = Channel.CreateUnbounded<int>();
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendAsync, exceptionTransformer: null);
        var handle = new RpcStreamHandle(9, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        Assert.Equal(RpcStreamManager.WindowSize, await ReadCreditAsync(credits));
        var frame = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 0x2A });

        Assert.True(streams.TryAcceptItem(handle.StreamId, frame));
        var chunk = await receiver.ReadChunkAsync(CancellationToken.None).AsTask().WaitAsync(Timeout);
        Assert.NotNull(chunk);

        chunk!.Dispose();

        Assert.Equal(1, await ReadCreditAsync(credits));
        streams.RemoveInbound(handle.StreamId);

        Task SendAsync(ReadOnlyMemory<byte> sentFrame, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (MessageFramer.TryReadFrameHeader(sentFrame, out _, out var type) &&
                type == MessageType.StreamCredit)
            {
                Assert.True(RpcRawFrame.TryReadInt32(sentFrame, out var count));
                credits.Writer.TryWrite(count);
            }

            return Task.CompletedTask;
        }
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private static async Task<int> ReadCreditAsync(Channel<int> credits) =>
        await credits.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

    private sealed class CountingReadStream(byte[] payload) : Stream
    {
        private int _disposeCount;
        private bool _read;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_read)
            {
                return ValueTask.FromResult(0);
            }

            _read = true;
            payload.AsSpan().CopyTo(buffer.Span);
            return ValueTask.FromResult(payload.Length);
        }

        public override ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposeCount);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

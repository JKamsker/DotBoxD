using System.Buffers;
using System.IO.Pipelines;
using ShaRPC.Core;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingWave7RegressionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PipeOutbound_WhenItemSendFails_AdvancesReadBuffer()
    {
        var serializer = new MessagePackRpcSerializer();
        var itemSendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 0));
        var streams = new RpcStreamManager(
            serializer,
            (frame, ct) =>
            {
                Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
                if (type == MessageType.StreamItem)
                {
                    itemSendStarted.TrySetResult();
                    throw new InvalidOperationException("Stream item send failed.");
                }

                return Task.CompletedTask;
            },
            exceptionTransformer: null);
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        await using var outbound = streams.RegisterOutbound(
            new[] { RpcStreamAttachment.FromPipe(handle, pipe) },
            CancellationToken.None);
        using var credit = RpcRawFrame.FrameInt32(handle.StreamId, MessageType.StreamCredit, 1);
        Assert.True(streams.TryAddCredit(credit));

        outbound.Start();
        pipe.Writer.Write(new byte[] { 1 });
        var flush = pipe.Writer.FlushAsync().AsTask();

        await itemSendStarted.Task.WaitAsync(TestTimeout);
        await flush.WaitAsync(TestTimeout);
        await outbound.WaitAsync().WaitAsync(TestTimeout);

        Assert.Equal(0, streams.OutboundSenderCount);
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task StartedOutboundSet_DisposeDisposesOwnedSourceWhenReadIgnoresCancellation()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var source = new CancellationIgnoringStream();
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        var outbound = streams.RegisterOutbound(
            new[] { RpcStreamAttachment.FromStream(handle, source, leaveOpen: false) },
            CancellationToken.None);
        outbound.Start();
        await source.ReadStarted.WaitAsync(TestTimeout);

        await outbound.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        Assert.True(source.Disposed);
        Assert.Equal(0, streams.OutboundSenderCount);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private sealed class CancellationIgnoringStream : Stream
    {
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> _readReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        public bool Disposed { get; private set; }

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
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            return new ValueTask<int>(_readReleased.Task);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            _readReleased.TrySetResult(0);
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            _readReleased.TrySetResult(0);
            return default;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

using System.Reflection;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound;

public sealed class RpcOutboundStreamSetStartDisposeRaceTests
{
    private static readonly FieldInfo StartedField =
        typeof(RpcOutboundStreamSet).GetField("_started", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("RpcOutboundStreamSet._started field was not found.");

    [Fact]
    public async Task DisposeAsync_DisposesOwnedSourceWhenStartWonBeforeTaskPublication()
    {
        var streams = new RpcStreamManager(new MessagePackRpcSerializer(), SendNoopAsync, exceptionTransformer: null);
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        var source = new CountingDisposeStream();
        var outbound = streams.RegisterOutbound(
            RpcStreamAttachment.FromStream(handle, source, leaveOpen: false),
            CancellationToken.None);

        MarkStartWonBeforeTasksPublished(outbound);

        await outbound.DisposeAsync();

        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(0, streams.OutboundSenderCount);
    }

    private static void MarkStartWonBeforeTasksPublished(RpcOutboundStreamSet outbound) =>
        StartedField.SetValue(outbound, 1);

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private sealed class CountingDisposeStream : Stream
    {
        private int _disposeCount;

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

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            new(0);

        public override ValueTask DisposeAsync()
        {
            CountDispose();
            return default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CountDispose();
            }
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private void CountDispose() =>
            Interlocked.Increment(ref _disposeCount);
    }
}

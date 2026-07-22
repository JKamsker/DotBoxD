using System.Threading.Tasks.Sources;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.StreamPooling;

public sealed class StreamFrameReceiveOperationTimeoutTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(30);

    [Fact]
    public async Task SynchronousReadTimeout_RemainsFaultedAndReleasesReceiveSlot()
    {
        var stream = new SynchronousTimeoutReadStream();
        await using var connection = CreateConnection(stream);

        var first = await Assert.ThrowsAsync<IOException>(
            () => connection.ReceiveFrameValueAsync().AsTask());
        var second = await Assert.ThrowsAsync<IOException>(
            () => connection.ReceiveFrameValueAsync().AsTask());

        Assert.Contains("stalled", first.Message);
        Assert.Contains("stalled", second.Message);
        Assert.Equal(2, stream.ReadCalls);
    }

    [Fact]
    public async Task SynchronousStatusTimeout_RemainsFaultedAndReleasesReceiveSlot()
    {
        var stream = new StatusTimeoutReadStream();
        await using var connection = CreateConnection(stream);

        var first = await Assert.ThrowsAsync<IOException>(
            () => connection.ReceiveFrameValueAsync().AsTask());
        var second = await Assert.ThrowsAsync<IOException>(
            () => connection.ReceiveFrameValueAsync().AsTask());

        Assert.Contains("stalled", first.Message);
        Assert.Contains("stalled", second.Message);
        Assert.Equal(2, stream.ReadCalls);
    }

    private static StreamConnection CreateConnection(Stream stream) =>
        new(stream, ownsStream: false, frameReadIdleTimeout: IdleTimeout);

    private static void WaitForCancellation(CancellationToken cancellationToken)
    {
        if (!cancellationToken.WaitHandle.WaitOne(Guard))
        {
            throw new TimeoutException("The frame-read idle timer did not cancel the read.");
        }
    }

    private sealed class SynchronousTimeoutReadStream : TimeoutReadStream
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            CountRead();
            WaitForCancellation(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The canceled read unexpectedly continued.");
        }
    }

    private sealed class StatusTimeoutReadStream : TimeoutReadStream
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            CountRead();
            return new ValueTask<int>(new StatusTimeoutSource(cancellationToken), token: 0);
        }

        private sealed class StatusTimeoutSource(CancellationToken cancellationToken)
            : IValueTaskSource<int>
        {
            public int GetResult(short token) => throw new InvalidOperationException();

            public ValueTaskSourceStatus GetStatus(short token)
            {
                WaitForCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException();
            }

            public void OnCompleted(
                Action<object?> continuation,
                object? state,
                short token,
                ValueTaskSourceOnCompletedFlags flags) =>
                throw new InvalidOperationException();
        }
    }

    private abstract class TimeoutReadStream : Stream
    {
        private int _readCalls;

        public int ReadCalls => Volatile.Read(ref _readCalls);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        protected void CountRead() => Interlocked.Increment(ref _readCalls);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

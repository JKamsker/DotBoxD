using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class StreamConnectionBorrowedStreamCloseRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task CloseAsync_WithActiveReceive_DoesNotDisposeBorrowedStream()
    {
        var stream = new BlockingDisposeTrackingStream();
        var connection = new StreamConnection(
            stream,
            ownsStream: false,
            frameReadIdleTimeout: System.Threading.Timeout.InfiniteTimeSpan);

        var receiveTask = connection.ReceiveAsync();
        await stream.WaitForReadAsync(Timeout);

        await connection.CloseAsync().WaitAsync(Timeout);
        var receiveException = await Record.ExceptionAsync(
            () => receiveTask.WaitAsync(Timeout));

        if (receiveException is not null)
        {
            Assert.True(
                receiveException is OperationCanceledException or ObjectDisposedException,
                $"Unexpected receive teardown exception: {receiveException}");
        }

        Assert.Equal(0, stream.DisposeCount);

        await stream.DisposeAsync();
    }

    private sealed class BlockingDisposeTrackingStream : Stream
    {
        private readonly CancellationTokenSource _disposeCts = new();
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public Task WaitForReadAsync(TimeSpan timeout) => _readStarted.Task.WaitAsync(timeout);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            _readStarted.TrySetResult();
            using var linked = ct.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);

            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, linked.Token).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask DisposeAsync()
        {
            if (!_disposeCts.IsCancellationRequested)
            {
                Interlocked.Increment(ref _disposeCount);
                _disposeCts.Cancel();
            }

            _disposeCts.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

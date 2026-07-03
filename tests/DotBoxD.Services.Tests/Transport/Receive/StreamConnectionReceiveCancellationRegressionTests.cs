using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class StreamConnectionReceiveCancellationRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task ReceiveAsync_WithPreCanceledToken_DoesNotStartReadOrOccupyReceiveSlot()
    {
        await using var stream = new PendingReadStream();
        await using var connection = new StreamConnection(
            stream,
            ownsStream: false,
            frameReadIdleTimeout: System.Threading.Timeout.InfiniteTimeSpan);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var firstReceive = connection.ReceiveAsync(cts.Token);
        var firstException = await Record.ExceptionAsync(
            () => firstReceive.WaitAsync(TimeSpan.FromMilliseconds(100)));
        var readCallsAfterPreCanceledReceive = stream.ReadCalls;

        Payload? secondPayload = null;
        var secondException = await Record.ExceptionAsync(async () =>
        {
            var secondReceive = connection.ReceiveAsync();
            if (!secondReceive.IsCompleted)
            {
                await stream.WaitForReadAsync(Timeout);
                stream.CompletePendingRead(0);
            }

            secondPayload = await secondReceive.WaitAsync(Timeout);
        });

        try
        {
            if (!firstReceive.IsCompleted)
            {
                stream.CompletePendingRead(0);
                using var ignored = await firstReceive.WaitAsync(Timeout);
            }

            Assert.IsAssignableFrom<OperationCanceledException>(firstException);
            Assert.Equal(0, readCallsAfterPreCanceledReceive);
            Assert.Null(secondException);
            Assert.Same(Payload.Empty, secondPayload);
        }
        finally
        {
            secondPayload?.Dispose();
        }
    }

    private sealed class PendingReadStream : Stream
    {
        private readonly TaskCompletionSource _readCalled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Queue<TaskCompletionSource<int>> _pendingReads = new();
        private int _readCalls;

        public int ReadCalls => Volatile.Read(ref _readCalls);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _readCalls);
            var pendingRead = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pendingReads)
            {
                _pendingReads.Enqueue(pendingRead);
            }

            _readCalled.TrySetResult();
            return new ValueTask<int>(pendingRead.Task);
        }

        public Task WaitForReadAsync(TimeSpan timeout) => _readCalled.Task.WaitAsync(timeout);

        public void CompletePendingRead(int bytesRead)
        {
            TaskCompletionSource<int>? pendingRead = null;
            lock (_pendingReads)
            {
                _pendingReads.TryDequeue(out pendingRead);
            }

            pendingRead?.TrySetResult(bytesRead);
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

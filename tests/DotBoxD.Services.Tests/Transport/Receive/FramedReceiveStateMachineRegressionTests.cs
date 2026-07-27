using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class FramedReceiveStateMachineRegressionTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(2);

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task ReceiveAsync_PreservesFrameAcrossPendingReadBoundaries(
        bool finiteTimeout,
        bool prefixPending,
        bool bodyPending)
    {
        var expected = CreateFrame();
        await using var stream = new PhaseReadStream(expected, prefixPending, bodyPending);
        await using var connection = CreateConnection(stream, finiteTimeout);

        var receive = connection.ReceiveAsync();
        await CompletePendingReadsAsync(stream, receive, prefixPending, bodyPending);

        using var received = await receive.WaitAsync(Guard);

        Assert.Equal(expected, received.Memory.ToArray());
        Assert.Equal(2, stream.ReadCalls);
        Assert.Equal(expected.Length, stream.BytesConsumed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiveAsync_CallerCancellationWhileTimerArmed_RemainsCancellation(
        bool cancelBodyRead)
    {
        var expected = CreateFrame();
        await using var stream = new PhaseReadStream(
            expected,
            prefixPending: !cancelBodyRead,
            bodyPending: cancelBodyRead);
        await using var connection = CreateConnection(stream, finiteTimeout: true);
        using var owner = new CancellationTokenSource();
        var receive = connection.ReceiveAsync(owner.Token);

        if (cancelBodyRead)
        {
            await stream.WaitForBodyReadAsync();
        }
        else
        {
            await stream.WaitForPrefixReadAsync();
        }

        owner.Cancel();

        var exception = await Record.ExceptionAsync(() => receive.WaitAsync(Guard));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(cancelBodyRead ? 2 : 1, stream.ReadCalls);
    }

    [Fact]
    public async Task DisposeAsync_DuringPartialBodyRead_DoesNotReadAgain()
    {
        var expected = CreateFrame();
        await using var stream = new PhaseReadStream(
            expected,
            prefixPending: false,
            bodyPending: true,
            bodyBytesPerRead: 1);
        await using var connection = CreateConnection(stream, finiteTimeout: true);
        var receive = connection.ReceiveFrameValueAsync().AsTask();

        await stream.WaitForBodyReadAsync();
        await connection.DisposeAsync();
        stream.ReleaseBodyRead();

        var exception = await Record.ExceptionAsync(() => receive.WaitAsync(Guard));

        Assert.IsType<ObjectDisposedException>(exception);
        Assert.Equal(2, stream.ReadCalls);
        Assert.Equal(5, stream.BytesConsumed);
    }

    private static StreamConnection CreateConnection(PhaseReadStream stream, bool finiteTimeout) =>
        new(
            stream,
            ownsStream: false,
            frameReadIdleTimeout: finiteTimeout
                ? TimeSpan.FromSeconds(30)
                : System.Threading.Timeout.InfiniteTimeSpan);

    private static async Task CompletePendingReadsAsync(
        PhaseReadStream stream,
        Task receive,
        bool prefixPending,
        bool bodyPending)
    {
        if (prefixPending)
        {
            await stream.WaitForPrefixReadAsync();
            Assert.False(receive.IsCompleted);
            stream.ReleasePrefixRead();
        }

        if (bodyPending)
        {
            await stream.WaitForBodyReadAsync();
            Assert.False(receive.IsCompleted);
            stream.ReleaseBodyRead();
        }
    }

    private static byte[] CreateFrame()
    {
        using var frame = MessageFramer.FrameToPayload(
            messageId: 41,
            MessageType.Response,
            new byte[] { 1, 2, 3, 4, 5 });
        return frame.Memory.ToArray();
    }

    private sealed class PhaseReadStream : Stream
    {
        private readonly byte[] _frame;
        private readonly bool _prefixPending;
        private readonly bool _bodyPending;
        private readonly int _bodyBytesPerRead;
        private readonly ReadPhase _prefix = new();
        private readonly ReadPhase _body = new();
        private int _offset;
        private int _readCalls;

        public PhaseReadStream(
            byte[] frame,
            bool prefixPending,
            bool bodyPending,
            int bodyBytesPerRead = int.MaxValue)
        {
            _frame = frame;
            _prefixPending = prefixPending;
            _bodyPending = bodyPending;
            _bodyBytesPerRead = bodyBytesPerRead;
        }

        public int ReadCalls => Volatile.Read(ref _readCalls);

        public int BytesConsumed => Volatile.Read(ref _offset);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _frame.Length;

        public override long Position
        {
            get => _offset;
            set => throw new NotSupportedException();
        }

        public Task WaitForPrefixReadAsync() => _prefix.Started.Task.WaitAsync(Guard);

        public Task WaitForBodyReadAsync() => _body.Started.Task.WaitAsync(Guard);

        public void ReleasePrefixRead() => _prefix.Release.TrySetResult();

        public void ReleaseBodyRead() => _body.Release.TrySetResult();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var readCall = Interlocked.Increment(ref _readCalls);
            var phase = readCall == 1 ? _prefix : _body;
            phase.Started.TrySetResult();

            var isPending = readCall == 1 ? _prefixPending : _bodyPending;
            return isPending
                ? AwaitReleaseAndCopyAsync(phase, buffer, readCall, cancellationToken)
                : ValueTask.FromResult(CopyTo(buffer, readCall));
        }

        private async ValueTask<int> AwaitReleaseAndCopyAsync(
            ReadPhase phase,
            Memory<byte> buffer,
            int readCall,
            CancellationToken cancellationToken)
        {
            await phase.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return CopyTo(buffer, readCall);
        }

        private int CopyTo(Memory<byte> buffer, int readCall)
        {
            var remaining = _frame.Length - _offset;
            if (remaining == 0)
            {
                return 0;
            }

            var limit = readCall == 1 ? 4 : _bodyBytesPerRead;
            var count = Math.Min(Math.Min(buffer.Length, remaining), limit);
            _frame.AsMemory(_offset, count).CopyTo(buffer);
            Interlocked.Add(ref _offset, count);
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private sealed class ReadPhase
        {
            public TaskCompletionSource Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}

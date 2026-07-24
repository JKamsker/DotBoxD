using System.Reflection;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class FrameReadTimeoutSourceDisposeRaceTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(2);

    private static readonly FieldInfo ConnectionTimeoutField =
        typeof(StreamConnection).GetField(
            "_frameReadTimeout",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("StreamConnection._frameReadTimeout was not found.");

    private static readonly FieldInfo CurrentSourceField =
        typeof(FrameReadTimeoutSource).GetField(
            "_source",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("FrameReadTimeoutSource._source was not found.");

    [Fact]
    public async Task DisposeAsync_DuringPartialRead_DoesNotRearmOrRetainTimeoutSource()
    {
        await using var stream = new PartialReadAfterDisposeStream();
        await using var connection = new StreamConnection(
            stream,
            ownsStream: false,
            frameReadIdleTimeout: TimeSpan.FromMinutes(10));
        using var owner = new CancellationTokenSource();
        var receive = connection.ReceiveFrameValueAsync(owner.Token).AsTask();

        await stream.WaitForFirstReadAsync(Guard);
        await connection.DisposeAsync();
        stream.ReleaseFirstRead();

        var exception = await Record.ExceptionAsync(() => receive.WaitAsync(Guard));
        await connection.DisposeAsync();

        Assert.IsType<ObjectDisposedException>(exception);
        Assert.Equal(1, stream.ReadCalls);
        Assert.False(HasCurrentTimeoutSource(connection));
    }

    private static bool HasCurrentTimeoutSource(StreamConnection connection)
    {
        var timeout = ConnectionTimeoutField.GetValue(connection)
            ?? throw new InvalidOperationException("The finite-timeout connection has no timeout source.");
        return CurrentSourceField.GetValue(timeout) is not null;
    }

    private sealed class PartialReadAfterDisposeStream : Stream
    {
        private readonly TaskCompletionSource _firstReadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCalls;

        public int ReadCalls => Volatile.Read(ref _readCalls);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public Task WaitForFirstReadAsync(TimeSpan timeout) =>
            _firstReadStarted.Task.WaitAsync(timeout);

        public void ReleaseFirstRead() => _releaseFirstRead.TrySetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _readCalls) != 1)
            {
                return 0;
            }

            _firstReadStarted.TrySetResult();
            await _releaseFirstRead.Task.ConfigureAwait(false);
            buffer.Span[0] = 1;
            return 1;
        }

        public override ValueTask DisposeAsync() => default;

        protected override void Dispose(bool disposing)
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

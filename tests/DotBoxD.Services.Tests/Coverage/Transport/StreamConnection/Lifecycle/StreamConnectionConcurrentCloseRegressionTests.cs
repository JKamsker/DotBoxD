using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class StreamConnectionConcurrentCloseRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RepeatedCloseAsync_WaitsForOwnedStreamDisposalToComplete()
    {
        var stream = new GatedDisposeStream();
        var connection = new StreamConnection(stream, ownsStream: true);
        var firstClose = connection.CloseAsync();

        await stream.DisposeEntered.WaitAsync(Timeout);

        var secondClose = connection.CloseAsync();
        Task? disposeTask = null;

        try
        {
            Assert.False(
                secondClose.IsCompleted,
                "Repeated CloseAsync completed before owned stream disposal finished.");

            disposeTask = connection.DisposeAsync().AsTask();
            Assert.False(
                disposeTask.IsCompleted,
                "DisposeAsync completed before owned stream disposal finished.");
        }
        finally
        {
            stream.ReleaseDispose();
            await firstClose.WaitAsync(Timeout);
            await secondClose.WaitAsync(Timeout);

            if (disposeTask is not null)
            {
                await disposeTask.WaitAsync(Timeout);
            }
        }
    }

    private sealed class GatedDisposeStream : Stream
    {
        private readonly TaskCompletionSource _disposeEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseDispose =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DisposeEntered => _disposeEntered.Task;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public void ReleaseDispose() => _releaseDispose.TrySetResult();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            ValueTask.FromResult(0);

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override async ValueTask DisposeAsync()
        {
            _disposeEntered.TrySetResult();
            await _releaseDispose.Task.ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}

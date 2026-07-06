using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcStreamAttachmentFactoryMutationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void AttachmentFactoriesRejectNullSources()
    {
        Assert.Equal(
            "stream",
            Assert.Throws<ArgumentNullException>(
                () => RpcStreamAttachment.FromStream(
                    new RpcStreamHandle(1, RpcStreamKind.Binary),
                    stream: null!)).ParamName);
        Assert.Equal(
            "pipe",
            Assert.Throws<ArgumentNullException>(
                () => RpcStreamAttachment.FromPipe(
                    new RpcStreamHandle(1, RpcStreamKind.Binary),
                    pipe: null!)).ParamName);
        Assert.Equal(
            "source",
            Assert.Throws<ArgumentNullException>(
                () => RpcStreamAttachment.FromAsyncEnumerable<int>(
                    new RpcStreamHandle(1, RpcStreamKind.Items),
                    source: null!)).ParamName);
    }

    [Fact]
    public async Task DisposeSourceBestEffortAsync_ReportsDisposeFailure()
    {
        var failure = new InvalidOperationException("dispose failed");
        var operation = "attachment-dispose-" + Guid.NewGuid().ToString("N");
        var observed = new TaskCompletionSource<RpcDiagnosticErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attachment = RpcStreamAttachment.FromStream(
            new RpcStreamHandle(2, RpcStreamKind.Binary),
            new ThrowingDisposeStream(failure),
            leaveOpen: false);

        void Handler(object? sender, RpcDiagnosticErrorEventArgs args)
        {
            if (args.Operation == operation && ReferenceEquals(args.Error, failure))
            {
                observed.TrySetResult(args);
            }
        }

        RpcDiagnostics.Error += Handler;
        try
        {
            await attachment.DisposeSourceBestEffortAsync(operation);

            var args = await observed.Task.WaitAsync(Timeout);
            Assert.Same(failure, args.Error);
            Assert.Equal(operation, args.Operation);
        }
        finally
        {
            RpcDiagnostics.Error -= Handler;
        }
    }

    private sealed class ThrowingDisposeStream(Exception failure) : Stream
    {
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
            ValueTask.FromResult(0);

        public override ValueTask DisposeAsync() => throw failure;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                throw failure;
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

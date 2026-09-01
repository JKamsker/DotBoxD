using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcStreamAttachmentPrimaryFailureTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Owned_source_cleanup_failure_does_not_mask_primary_read_failure()
    {
        var serializer = new MessagePackRpcSerializer();
        var streamError = new TaskCompletionSource<RpcResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pumpDiagnostic = new TaskCompletionSource<RpcDiagnosticErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new ReadAndDisposeFailingStream();
        var expectedStreamId = 0;
        var streams = new RpcStreamManager(
            serializer,
            SendFrameAsync,
            exceptionTransformer: static ex => new RpcErrorInfo(ex.Message, ex.GetType().Name));
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        expectedStreamId = handle.StreamId;
        await using var outbound = streams.RegisterOutbound(
            RpcStreamAttachment.FromStream(handle, source, leaveOpen: false),
            CancellationToken.None);

        RpcDiagnostics.Error += OnDiagnostic;
        try
        {
            outbound.Start();

            var diagnostic = await pumpDiagnostic.Task.WaitAsync(Timeout);
            var terminal = await streamError.Task.WaitAsync(Timeout);
            await outbound.WaitAsync().WaitAsync(Timeout);

            await outbound.DisposeAsync();
            Assert.Equal(1, source.DisposeCount);
            Assert.Equal(ReadAndDisposeFailingStream.PrimaryFailureMessage, diagnostic.Error.Message);
            Assert.Equal(ReadAndDisposeFailingStream.PrimaryFailureMessage, terminal.ErrorMessage);
            Assert.Contains("InvalidOperationException", terminal.ErrorType, StringComparison.Ordinal);
        }
        finally
        {
            RpcDiagnostics.Error -= OnDiagnostic;
        }

        Task SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
        {
            Assert.True(
                MessageFramer.TryReadFrame(frame, out var streamId, out var type, out var envelope, out _));
            if (streamId == expectedStreamId && type == MessageType.StreamError)
            {
                streamError.TrySetResult(serializer.Deserialize<RpcResponse>(envelope));
            }

            return Task.CompletedTask;
        }

        void OnDiagnostic(object? sender, RpcDiagnosticErrorEventArgs args)
        {
            if (args.Operation == "Outbound stream pump failed")
            {
                pumpDiagnostic.TrySetResult(args);
            }
        }
    }

    [Fact]
    public async Task Async_enumerator_cleanup_failure_does_not_mask_primary_iteration_failure()
    {
        var serializer = new MessagePackRpcSerializer();
        var streamError = new TaskCompletionSource<RpcResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pumpDiagnostic = new TaskCompletionSource<RpcDiagnosticErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new MoveNextAndDisposeFailingAsyncEnumerable();
        var expectedStreamId = 0;
        var streams = new RpcStreamManager(
            serializer,
            SendFrameAsync,
            exceptionTransformer: static ex => new RpcErrorInfo(ex.Message, ex.GetType().Name));
        var handle = streams.ReserveOutbound(RpcStreamKind.Items);
        expectedStreamId = handle.StreamId;
        await using var outbound = streams.RegisterOutbound(
            RpcStreamAttachment.FromAsyncEnumerable(handle, source),
            CancellationToken.None);

        RpcDiagnostics.Error += OnDiagnostic;
        try
        {
            outbound.Start();

            var diagnostic = await pumpDiagnostic.Task.WaitAsync(Timeout);
            var terminal = await streamError.Task.WaitAsync(Timeout);
            await outbound.WaitAsync().WaitAsync(Timeout);

            Assert.Equal(1, source.DisposeCount);
            Assert.Equal(MoveNextAndDisposeFailingAsyncEnumerable.PrimaryFailureMessage, diagnostic.Error.Message);
            Assert.Equal(MoveNextAndDisposeFailingAsyncEnumerable.PrimaryFailureMessage, terminal.ErrorMessage);
            Assert.Contains("InvalidOperationException", terminal.ErrorType, StringComparison.Ordinal);
        }
        finally
        {
            RpcDiagnostics.Error -= OnDiagnostic;
        }

        Task SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
        {
            Assert.True(
                MessageFramer.TryReadFrame(frame, out var streamId, out var type, out var envelope, out _));
            if (streamId == expectedStreamId && type == MessageType.StreamError)
            {
                streamError.TrySetResult(serializer.Deserialize<RpcResponse>(envelope));
            }

            return Task.CompletedTask;
        }

        void OnDiagnostic(object? sender, RpcDiagnosticErrorEventArgs args)
        {
            if (args.Operation == "Outbound stream pump failed")
            {
                pumpDiagnostic.TrySetResult(args);
            }
        }
    }

    private sealed class ReadAndDisposeFailingStream : Stream
    {
        public const string PrimaryFailureMessage = "primary source read failure";
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

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException(PrimaryFailureMessage);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new InvalidOperationException(PrimaryFailureMessage));

        public override ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.FromException(new InvalidOperationException("source cleanup failure"));
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class MoveNextAndDisposeFailingAsyncEnumerable : IAsyncEnumerable<int>
    {
        public const string PrimaryFailureMessage = "primary iteration failure";
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(this);

        private sealed class Enumerator(MoveNextAndDisposeFailingAsyncEnumerable owner) : IAsyncEnumerator<int>
        {
            public int Current => throw new InvalidOperationException("No item is produced.");

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner._disposeCount);
                return ValueTask.FromException(new InvalidOperationException("enumerator cleanup failure"));
            }

            public ValueTask<bool> MoveNextAsync() =>
                ValueTask.FromException<bool>(new InvalidOperationException(PrimaryFailureMessage));
        }
    }
}

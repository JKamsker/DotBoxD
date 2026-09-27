using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Streaming.Remote;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound;

public sealed class OutboundStreamInvocationCallbackDeadlockTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Successful_response_completes_when_stream_cancellation_callback_awaits_invocation()
    {
        var source = new InvocationJoiningStream();
        var dispatcher = new RespondAfterReadStartsDispatcher(source.ReadStarted);
        var (left, right) = InMemoryPipe.CreateConnectionPair();
        var serializer = new MessagePackRpcSerializer();
        await using var server = RpcPeer.Over(right, serializer).Provide(dispatcher).Start();
        await using var client = RpcPeer.Over(left, serializer).Start();
        var handle = client.ReserveStream(RpcStreamKind.Binary);
        var attachment = RpcStreamAttachment.FromStream(
            handle,
            source,
            leaveOpen: false);
        var invocation = client.InvokeAsync<RpcStreamHandle, int>("Completion", "Return", handle, attachment);
        source.SetInvocation(invocation);

        try
        {
            await source.ReadStarted.WaitAsync(Timeout);
            await source.CallbackEntered.WaitAsync(Timeout);

            Assert.Equal(42, await invocation.WaitAsync(Timeout));
            await source.Disposed.WaitAsync(Timeout);
            Assert.Equal(1, source.DisposeCount);
        }
        finally
        {
            source.AllowCallbackToReturn();
            await invocation.WaitAsync(Timeout);
        }
    }

    private sealed class RespondAfterReadStartsDispatcher(Task readStarted) : IServiceDispatcher
    {
        public string ServiceName => "Completion";

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            IRpcStreamingContext streaming,
            CancellationToken ct = default)
        {
            var handle = serializer.Deserialize<RpcStreamHandle>(payload);
            await using var stream = streaming.GetStream(handle);
            await readStarted.WaitAsync(ct);
            serializer.Serialize(output, 42);
        }
    }

    private sealed class InvocationJoiningStream : Stream
    {
        private readonly TaskCompletionSource _callbackEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _callbackMayReturn =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> _read =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _cancellationRegistration;
        private Task? _invocation;
        private int _disposeCount;

        public Task CallbackEntered => _callbackEntered.Task;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task Disposed => _disposed.Task;

        public Task ReadStarted => _readStarted.Task;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void AllowCallbackToReturn() => _callbackMayReturn.TrySetResult();

        public void SetInvocation(Task invocation) => _invocation = invocation;

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _cancellationRegistration = cancellationToken.Register(OnCanceled);
            _readStarted.TrySetResult();
            return new ValueTask<int>(_read.Task);
        }

        public override ValueTask DisposeAsync()
        {
            if (Interlocked.Increment(ref _disposeCount) == 1)
            {
                _cancellationRegistration.Dispose();
                _read.TrySetResult(0);
                _disposed.TrySetResult();
            }

            return default;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private void OnCanceled()
        {
            _callbackEntered.TrySetResult();
            Task.WaitAny(_invocation ?? throw new InvalidOperationException("Invocation was not assigned."), _callbackMayReturn.Task);
        }
    }
}

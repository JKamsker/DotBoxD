using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle;

public sealed class RpcPeerDisposeLateStreamItemRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DisposeAsync_DropsLateStreamItemsReturnedAfterReadLoopCancellation()
    {
        var serializer = new MessagePackRpcSerializer();
        var channel = new LateStreamItemAfterDisposeChannel(serializer);
        var peer = RpcPeer
            .Over(channel, serializer, new RpcPeerOptions { RequestTimeout = Timeout })
            .Start();

        Stream? remoteStream = null;
        Task? disposeTask = null;
        try
        {
            await channel.WaitForInitialReceiveAsync(Timeout);
            var invokeTask = peer.InvokeStreamAsync("Streaming", "Download");
            await channel.WaitForRequestAsync(Timeout);
            remoteStream = await invokeTask.WaitAsync(Timeout);
            await channel.WaitForLateReceiveAsync(Timeout);

            disposeTask = peer.DisposeAsync().AsTask();
            await channel.WaitForDisposeStartedAsync(Timeout);
            await channel.WaitForLateFrameReturnedAsync(Timeout);

            var buffer = new byte[1];
            var readTask = remoteStream.ReadAsync(buffer, 0, buffer.Length);
            var earlyCompletion = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromMilliseconds(500)));

            Assert.NotSame(readTask, earlyCompletion);

            channel.AllowDispose();
            await disposeTask.WaitAsync(Timeout);

            var error = await Record.ExceptionAsync(async () => await readTask.WaitAsync(Timeout));
            Assert.True(
                error is ServiceConnectionException or OperationCanceledException,
                $"Expected the stream read to fail as connection shutdown, but saw {error?.GetType().Name ?? "no exception"}.");
        }
        finally
        {
            remoteStream?.Dispose();
            channel.AllowDispose();
            if (disposeTask is not null)
            {
                await disposeTask.WaitAsync(Timeout);
            }
            else
            {
                await peer.DisposeAsync().AsTask().WaitAsync(Timeout);
            }
        }
    }

    private sealed class LateStreamItemAfterDisposeChannel : IRpcChannel
    {
        private const int StreamId = 51_001;

        private readonly MessagePackRpcSerializer _serializer;
        private readonly TaskCompletionSource<bool> _initialReceiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> _requestMessageId = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _lateReceiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _disposeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _lateFrameReturned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allowDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposed;
        private int _receiveCount;

        public LateStreamItemAfterDisposeChannel(MessagePackRpcSerializer serializer) =>
            _serializer = serializer;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "late-stream-item://test";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (MessageFramer.TryReadFrameHeader(data, out var messageId, out var messageType) &&
                messageType == MessageType.Request)
            {
                _requestMessageId.TrySetResult(messageId);
            }

            return Task.CompletedTask;
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            var receive = Interlocked.Increment(ref _receiveCount);
            if (receive == 1)
            {
                _initialReceiveStarted.TrySetResult(true);
                var messageId = await _requestMessageId.Task.WaitAsync(ct).ConfigureAwait(false);
                return CreateStreamResponseFrame(messageId);
            }

            if (receive == 2)
            {
                _lateReceiveStarted.TrySetResult(true);
                await WaitForCancellationAsync(ct).ConfigureAwait(false);
                var frame = MessageFramer.FrameToPayload(
                    StreamId,
                    MessageType.StreamItem,
                    new byte[] { 0x5a });
                _lateFrameReturned.TrySetResult(true);
                return frame;
            }

            await Task.Delay(-1, ct).ConfigureAwait(false);
            return Payload.Empty;
        }

        public async ValueTask DisposeAsync()
        {
            _disposeStarted.TrySetResult(true);
            await _allowDispose.Task.ConfigureAwait(false);
            Volatile.Write(ref _disposed, 1);
        }

        public void AllowDispose() => _allowDispose.TrySetResult(true);

        public Task WaitForInitialReceiveAsync(TimeSpan timeout) =>
            _initialReceiveStarted.Task.WaitAsync(timeout);

        public Task WaitForRequestAsync(TimeSpan timeout) =>
            _requestMessageId.Task.WaitAsync(timeout);

        public Task WaitForLateReceiveAsync(TimeSpan timeout) =>
            _lateReceiveStarted.Task.WaitAsync(timeout);

        public Task WaitForDisposeStartedAsync(TimeSpan timeout) =>
            _disposeStarted.Task.WaitAsync(timeout);

        public Task WaitForLateFrameReturnedAsync(TimeSpan timeout) =>
            _lateFrameReturned.Task.WaitAsync(timeout);

        private Payload CreateStreamResponseFrame(int messageId)
        {
            var response = new RpcResponse
            {
                MessageId = messageId,
                IsSuccess = true,
                Stream = new RpcStreamHandle(StreamId, RpcStreamKind.Binary),
            };

            return MessageFramer.FrameMessage(
                _serializer,
                messageId,
                MessageType.Response,
                response,
                ReadOnlySpan<byte>.Empty);
        }

        private static async Task WaitForCancellationAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ct.Register(static state =>
                ((TaskCompletionSource<bool>)state!).TrySetResult(true), canceled);
            await canceled.Task.ConfigureAwait(false);
        }
    }
}

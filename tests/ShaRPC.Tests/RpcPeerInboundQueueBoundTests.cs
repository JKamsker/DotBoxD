using System.Buffers;
using System.Threading.Channels;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class RpcPeerInboundQueueBoundTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task WaitQueue_DoesNotReadPastConfiguredCapacity()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var dispatcher = new BlockingDispatcher();

        for (var id = 1; id <= 4; id++)
        {
            connection.Enqueue(CreateRequestFrame(serializer, id));
        }

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    QueueFullMode = ShaRpcQueueFullMode.Wait,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.FirstEntered.WaitAsync(TimeSpan.FromSeconds(1));
        await connection.WaitForReceiveCountAsync(3, TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        Assert.Equal(3, connection.ReceiveCount);

        dispatcher.Release();
        await connection.WaitForReceiveCountAsync(4, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DropIncoming_ReleasesDroppedFrame()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var dispatcher = new BlockingDispatcher();
        var first = CreateRequestFrame(serializer, 1);
        var second = CreateRequestFrame(serializer, 2);
        connection.Enqueue(first);
        connection.Enqueue(second);

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    QueueFullMode = ShaRpcQueueFullMode.DropIncoming,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.FirstEntered.WaitAsync(TimeSpan.FromSeconds(1));
        await connection.WaitForReceiveCountAsync(2, TimeSpan.FromSeconds(1));
        await AssertDisposedAsync(second);

        dispatcher.Release();
    }

    [Fact]
    public async Task RejectInboundCalls_ReturnsExplicitErrorResponse()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var peer = RpcPeer
            .Over(
                serverConnection,
                serializer,
                new RpcPeerOptions
                {
                    RejectInboundCalls = true,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Start();

        using var requestFrame = CreateRequestFrame(serializer, 42);
        await client.SendAsync(requestFrame.Memory);

        using var responseFrame = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(MessageFramer.TryReadFrame(
            responseFrame.Memory,
            out var messageId,
            out var messageType,
            out var envelope,
            out var payload));
        var response = serializer.Deserialize<RpcResponse>(envelope);

        Assert.Equal(42, messageId);
        Assert.Equal(MessageType.Error, messageType);
        Assert.Equal(0, payload.Length);
        Assert.False(response.IsSuccess);
        Assert.Equal("ShaRpcInboundRejected", response.ErrorType);
        Assert.Equal("This peer does not accept inbound calls.", response.ErrorMessage);
    }

    private static Payload CreateRequestFrame(ISerializer serializer, int messageId) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = messageId,
                ServiceName = BlockingDispatcher.Service,
                MethodName = "Hold",
            },
            ReadOnlySpan<byte>.Empty);

    private static async Task AssertDisposedAsync(Payload frame)
    {
        var timeoutAt = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (DateTime.UtcNow < timeoutAt)
        {
            try
            {
                _ = frame.Memory;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        Assert.Throws<ObjectDisposedException>(() => frame.Memory);
    }

    private sealed class BlockingDispatcher : IServiceDispatcher
    {
        public const string Service = "Blocking";

        private readonly TaskCompletionSource<bool> _firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ServiceName => Service;

        public Task FirstEntered => _firstEntered.Task;

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            _firstEntered.TrySetResult(true);
            await _release.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public void Release() => _release.TrySetResult(true);
    }

    private sealed class ScriptedConnection : IConnection
    {
        private readonly Channel<Payload> _inbound = Channel.CreateUnbounded<Payload>(
            new UnboundedChannelOptions { SingleReader = true });
        private readonly List<(int Count, TaskCompletionSource<bool> Completion)> _waiters = new();
        private int _disposed;
        private int _receiveCount;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "scripted://remote";

        public int ReceiveCount => Volatile.Read(ref _receiveCount);

        public void Enqueue(Payload frame) => _inbound.Writer.TryWrite(frame);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.CompletedTask;

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                var frame = await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
                CompleteWaiters(Interlocked.Increment(ref _receiveCount));
                return frame;
            }
            catch (ChannelClosedException)
            {
                return Payload.Empty;
            }
        }

        public Task WaitForReceiveCountAsync(int count, TimeSpan timeout)
        {
            if (ReceiveCount >= count)
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_waiters)
            {
                if (ReceiveCount >= count)
                {
                    return Task.CompletedTask;
                }

                _waiters.Add((count, completion));
            }

            return completion.Task.WaitAsync(timeout);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return default;
            }

            _inbound.Writer.TryComplete();
            while (_inbound.Reader.TryRead(out var frame))
            {
                frame.Dispose();
            }

            return default;
        }

        private void CompleteWaiters(int count)
        {
            List<TaskCompletionSource<bool>>? completed = null;
            lock (_waiters)
            {
                for (var i = _waiters.Count - 1; i >= 0; i--)
                {
                    if (count < _waiters[i].Count)
                    {
                        continue;
                    }

                    completed ??= new List<TaskCompletionSource<bool>>();
                    completed.Add(_waiters[i].Completion);
                    _waiters.RemoveAt(i);
                }
            }

            if (completed is null)
            {
                return;
            }

            foreach (var completion in completed)
            {
                completion.TrySetResult(true);
            }
        }
    }
}

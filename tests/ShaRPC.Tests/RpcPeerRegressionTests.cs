using System.Buffers;
using ShaRPC.Core;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using Shared;
using Xunit;

namespace ShaRPC.Tests;

public class RpcPeerRegressionTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task PendingCall_FailsWithConnectionException_WhenRemoteClosesCleanly()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30) })
            .Start();

        var call = client.InvokeAsync<int>("MissingService", "NeverCompletes");

        using (await serverConnection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1)))
        {
        }

        await serverConnection.DisposeAsync();

        await Assert.ThrowsAsync<ShaRpcConnectionException>(
            () => call.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task StopAsync_ClosesAcceptedPeers()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer())
            .ForEachPeer(peer => peer.Provide<IGameService>(new TestGameService()));
        host.PeerConnected += peer => connected.TrySetResult(peer);
        await host.StartAsync();

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();
        var game = client.GetGameService();

        Assert.NotNull(await game.GetServerStatusAsync());
        var acceptedPeer = await connected.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await host.StopAsync();

        Assert.False(acceptedPeer.IsConnected);
        await Assert.ThrowsAsync<ShaRpcConnectionException>(
            () => game.GetServerStatusAsync().WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task InboundQueueCapacity_WaitMode_LimitsDispatchConcurrency()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var dispatcher = new BlockingDispatcher();

        await using var server = RpcPeer
            .Over(
                serverConnection,
                NewSerializer(),
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    QueueFullMode = ShaRpcQueueFullMode.Wait,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        var calls = Enumerable.Range(0, 3)
            .Select(_ => client.InvokeAsync(BlockingDispatcher.Service, "Hold"))
            .ToArray();

        try
        {
            await dispatcher.FirstEntered.WaitAsync(TimeSpan.FromSeconds(1));

            var secondDispatch = await Task.WhenAny(
                dispatcher.SecondEntered,
                Task.Delay(TimeSpan.FromMilliseconds(200)));

            Assert.NotSame(dispatcher.SecondEntered, secondDispatch);
            Assert.Equal(1, dispatcher.MaxActive);
        }
        finally
        {
            dispatcher.Release();
        }

        await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task InboundQueueCapacity_DropIncoming_DropsFramesBeyondBoundedQueue()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var dispatcher = new BlockingDispatcher();

        await using var server = RpcPeer
            .Over(
                serverConnection,
                NewSerializer(),
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    QueueFullMode = ShaRpcQueueFullMode.DropIncoming,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromMilliseconds(750) })
            .Start();

        var calls = Enumerable.Range(0, 3)
            .Select(_ => client.InvokeAsync(BlockingDispatcher.Service, "Hold"))
            .ToArray();

        await dispatcher.FirstEntered.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        dispatcher.Release();

        var outcomes = await Task.WhenAll(calls.Select(CaptureExceptionAsync))
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, dispatcher.MaxActive);
        Assert.Equal(2, outcomes.Count(static exception => exception is null));
        Assert.Single(outcomes.OfType<ShaRpcTimeoutException>());
    }

    private static async Task<Exception?> CaptureExceptionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class BlockingDispatcher : IServiceDispatcher
    {
        public const string Service = "Blocking";

        private readonly TaskCompletionSource<bool> _firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _secondEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _maxActive;
        private int _started;

        public string ServiceName => Service;

        public Task FirstEntered => _firstEntered.Task;

        public Task SecondEntered => _secondEntered.Task;

        public int MaxActive => Volatile.Read(ref _maxActive);

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            var started = Interlocked.Increment(ref _started);
            if (started == 1)
            {
                _firstEntered.TrySetResult(true);
            }
            else if (started == 2)
            {
                _secondEntered.TrySetResult(true);
            }

            var active = Interlocked.Increment(ref _active);
            RecordMaxActive(active);
            try
            {
                await _release.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public void Release() => _release.TrySetResult(true);

        private void RecordMaxActive(int active)
        {
            while (true)
            {
                var maxActive = Volatile.Read(ref _maxActive);
                if (active <= maxActive)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxActive, active, maxActive) == maxActive)
                {
                    return;
                }
            }
        }
    }
}

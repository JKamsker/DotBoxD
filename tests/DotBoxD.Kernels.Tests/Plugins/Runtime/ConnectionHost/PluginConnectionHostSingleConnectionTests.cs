using System.Threading.Channels;
using DotBoxD.Plugins;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Testing;
using DotBoxD.Services.Transport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginConnectionHostSingleConnectionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task Later_peer_is_rejected_after_first_connection_is_established()
    {
        var (firstServerChannel, firstClientChannel) = InMemoryRpcChannel.CreatePair();
        var (secondServerChannel, secondClientChannel) = InMemoryRpcChannel.CreatePair();
        using var server = PluginServer.Create();
        var transport = new QueuedServerTransport();
        transport.Enqueue(firstServerChannel);

        await using var host = await PluginConnectionHost<object>.StartAsync(
            server,
            transport,
            static (_, _) => new object());

        await using var firstSession = await RpcMessagePackIpc.ConnectAsync(
            new SingleConnectionTransport(firstClientChannel, ownsConnection: true),
            new RpcPeerOptions { RequestTimeout = Timeout });

        await host.Connected.WaitAsync(Timeout);

        transport.Enqueue(secondServerChannel);
        RpcPeerSession? secondSession = null;
        var secondConnectFailure = await Record.ExceptionAsync(async () =>
            secondSession = await RpcMessagePackIpc.ConnectAsync(
                    new SingleConnectionTransport(secondClientChannel, ownsConnection: true),
                    new RpcPeerOptions { RequestTimeout = Timeout })
                .WaitAsync(Timeout));

        try
        {
            if (secondConnectFailure is not null)
            {
                Assert.IsNotType<TimeoutException>(secondConnectFailure);
                return;
            }

            Assert.NotNull(secondSession);
            await transport.WaitForAcceptedCountAsync(2, Timeout);
            await WaitUntilAsync(
                () => !secondSession.IsConnected || !secondServerChannel.IsConnected,
                Timeout,
                "The second PluginConnectionHost peer stayed connected instead of being rejected.");
        }
        finally
        {
            if (secondSession is not null)
            {
                await secondSession.DisposeAsync();
            }
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string timeoutMessage)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            try
            {
                await Task.Delay(PollInterval, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                break;
            }
        }

        if (!condition())
        {
            throw new TimeoutException(timeoutMessage);
        }
    }

    private sealed class QueuedServerTransport : IServerTransport
    {
        private readonly Channel<IRpcChannel> _connections =
            Channel.CreateUnbounded<IRpcChannel>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        private readonly object _acceptedLock = new();
        private TaskCompletionSource _acceptedChanged =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _acceptedCount;
        private int _disposed;

        public void Enqueue(IRpcChannel connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            if (!_connections.Writer.TryWrite(connection))
            {
                throw new InvalidOperationException("Transport is closed.");
            }
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(QueuedServerTransport));
            }

            return Task.CompletedTask;
        }

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(QueuedServerTransport));
            }

            try
            {
                var connection = await _connections.Reader.ReadAsync(ct).ConfigureAwait(false);
                PublishAccepted();
                return connection;
            }
            catch (ChannelClosedException) when (ct.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
            {
                throw new OperationCanceledException(ct);
            }
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public async Task WaitForAcceptedCountAsync(int count, TimeSpan timeout)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            try
            {
                while (Volatile.Read(ref _acceptedCount) < count)
                {
                    Task waitTask;
                    lock (_acceptedLock)
                    {
                        if (_acceptedCount >= count)
                        {
                            return;
                        }

                        waitTask = _acceptedChanged.Task;
                    }

                    await waitTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Transport accepted {Volatile.Read(ref _acceptedCount)} connection(s), expected {count}.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _connections.Writer.TryComplete();
            while (_connections.Reader.TryRead(out var pending))
            {
                await pending.DisposeAsync().ConfigureAwait(false);
            }
        }

        private void PublishAccepted()
        {
            lock (_acceptedLock)
            {
                _acceptedCount++;
                _acceptedChanged.TrySetResult();
                _acceptedChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }
}

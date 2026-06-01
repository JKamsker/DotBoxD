using System.Collections.Concurrent;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

/// <summary>
/// Accepts connections from a listener and turns each one into an <see cref="RpcPeer"/>. The
/// accept loop that used to live inside the server now lives here, and its output is peers:
/// because each connection is a full peer, a host can both provide services to and call back
/// into the peers that connect to it.
/// </summary>
public sealed class RpcHost : IAsyncDisposable
{
    private static readonly TimeSpan AcceptErrorBackoff = TimeSpan.FromMilliseconds(50);

    private readonly IServerTransport _listener;
    private readonly ISerializer _serializer;
    private readonly RpcPeerOptions _options;
    private readonly object _lifecycleLock = new();
    private readonly List<Action<RpcPeer>> _configure = new();
    private readonly ConcurrentDictionary<RpcPeer, byte> _peers = new();
    private readonly ConcurrentDictionary<Task, byte> _peerCleanupTasks = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private Task? _stopTask;
    private bool _starting;
    private int _disposed;

    private RpcHost(IServerTransport listener, ISerializer serializer, RpcPeerOptions options)
    {
        _listener = listener;
        _serializer = serializer;
        _options = options;
    }

    /// <summary>Creates a host that turns every accepted connection into a peer.</summary>
    public static RpcHost Listen(IServerTransport listener, ISerializer serializer, RpcPeerOptions? options = null)
    {
        if (listener is null)
        {
            throw new ArgumentNullException(nameof(listener));
        }

        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        return new RpcHost(listener, serializer, options ?? new RpcPeerOptions());
    }

    /// <summary>Registers configuration that runs for every accepted peer before its read loop
    /// starts. Use it to <see cref="RpcPeer.Provide{TService}(TService)"/> exports (and optionally
    /// <see cref="RpcPeer.Get{TService}"/> proxies to call the peer back).</summary>
    /// <remarks>
    /// Services provided here are callable by any accepted peer. ShaRPC does not add
    /// authentication or authorization; enforce access control at the transport or application
    /// layer.
    /// </remarks>
    public RpcHost ForEachPeer(Action<RpcPeer> configure)
    {
        _configure.Add(configure ?? throw new ArgumentNullException(nameof(configure)));
        return this;
    }

    /// <summary>Raised after a connection is accepted and configured.</summary>
    public event EventHandler<RpcPeerEventArgs>? PeerConnected;

    /// <summary>Raised when an accepted peer's read loop ends.</summary>
    public event EventHandler<RpcPeerEventArgs>? PeerDisconnected;

    /// <summary>Raised when the host accept loop catches a non-cancellation exception.</summary>
    public event EventHandler<RpcHostErrorEventArgs>? AcceptError;

    public async Task StartAsync(CancellationToken ct = default)
    {
        CancellationTokenSource cts;
        lock (_lifecycleLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(RpcHost));
            }

            if (_cts is not null || _starting)
            {
                throw new InvalidOperationException("Host is already running.");
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cts = cts;
            _starting = true;
        }

        try
        {
            await _listener.StartAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                    _stopTask = null;
                    cts.Dispose();
                }

                _starting = false;
            }

            throw;
        }

        var stopStartedListener = false;
        Exception? startFailure = null;
        var disposeCts = false;
        lock (_lifecycleLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                var ownsCts = ReferenceEquals(_cts, cts);
                disposeCts = ownsCts && _stopTask is null;
                if (ownsCts)
                {
                    _cts = null;
                    _stopTask = null;
                }

                stopStartedListener = true;
                startFailure = new ObjectDisposedException(nameof(RpcHost));
            }
            else if (!ReferenceEquals(_cts, cts))
            {
                stopStartedListener = true;
            }
            else if (_stopTask is not null || cts.IsCancellationRequested)
            {
                stopStartedListener = true;
            }
            else
            {
                _starting = false;
                _acceptTask = AcceptLoopAsync(cts.Token);
                return;
            }
        }

        try
        {
            if (stopStartedListener)
            {
                await _listener.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _starting = false;
            }
        }

        if (startFailure is not null)
        {
            if (disposeCts)
            {
                cts.Dispose();
            }

            throw startFailure;
        }

        throw new InvalidOperationException("Host start was stopped before it completed.");
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        lock (_lifecycleLock)
        {
            if (_cts is null)
            {
                return Task.CompletedTask;
            }

            return _stopTask ??= StopCoreAsync(_cts, _acceptTask, ct);
        }
    }

    private async Task StopCoreAsync(CancellationTokenSource cts, Task? acceptTask, CancellationToken ct)
    {
        cts.Cancel();

        if (acceptTask is not null)
        {
            try
            {
                await acceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _listener.StopAsync(ct).ConfigureAwait(false);
        await ClosePeersAsync().ConfigureAwait(false);
        await AwaitPeerCleanupTasksAsync().ConfigureAwait(false);
        cts.Dispose();

        lock (_lifecycleLock)
        {
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
                _acceptTask = null;
                _stopTask = null;
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IConnection connection;
            try
            {
                connection = await _listener.AcceptAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                RpcEventHandlerInvoker.Raise(AcceptError, this, new RpcHostErrorEventArgs(ex));
                try
                {
                    await Task.Delay(AcceptErrorBackoff, ct).ConfigureAwait(false);
                    continue;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }

            await AddPeerAsync(connection).ConfigureAwait(false);
        }
    }

    private async Task AddPeerAsync(IConnection connection)
    {
        var peer = RpcPeer.Over(connection, _serializer, _options);
        try
        {
            foreach (var configure in _configure)
            {
                configure(peer);
            }
        }
        catch
        {
            await peer.DisposeAsync().ConfigureAwait(false);
            return;
        }

        _peers.TryAdd(peer, 0);
        peer.Disconnected += OnPeerDisconnected;
        peer.Start();
        RpcEventHandlerInvoker.Raise(PeerConnected, this, new RpcPeerEventArgs(peer));
    }

    private void OnPeerDisconnected(object? sender, RpcDisconnectedEventArgs args)
    {
        if (sender is not RpcPeer peer)
        {
            return;
        }

        _peers.TryRemove(peer, out _);
        RpcEventHandlerInvoker.Raise(PeerDisconnected, this, new RpcPeerEventArgs(peer));

        // Dispose off the read-loop callback so DisposeAsync can await the now-completing loop
        // without deadlocking on itself.
        var cleanupTask = Task.Run(async () =>
        {
            try
            {
                await peer.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup.
            }
        });
        _peerCleanupTasks.TryAdd(cleanupTask, 0);
        _ = cleanupTask.ContinueWith(
            static (task, state) =>
                ((ConcurrentDictionary<Task, byte>)state!).TryRemove(task, out _),
            _peerCleanupTasks,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        await _listener.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ClosePeersAsync()
    {
        foreach (var peer in _peers.Keys)
        {
            try
            {
                await peer.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        _peers.Clear();
    }

    private async Task AwaitPeerCleanupTasksAsync()
    {
        var tasks = _peerCleanupTasks.Keys.ToArray();
        if (tasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // Peer cleanup is best-effort and each task observes its own dispose failures.
        }
    }
}

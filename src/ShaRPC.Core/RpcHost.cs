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
    private readonly IServerTransport _listener;
    private readonly ISerializer _serializer;
    private readonly RpcPeerOptions _options;
    private readonly List<Action<RpcPeer>> _configure = new();
    private readonly ConcurrentDictionary<RpcPeer, byte> _peers = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
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
    public RpcHost ForEachPeer(Action<RpcPeer> configure)
    {
        _configure.Add(configure ?? throw new ArgumentNullException(nameof(configure)));
        return this;
    }

    /// <summary>Raised after a connection is accepted and configured.</summary>
    public event Action<RpcPeer>? PeerConnected;

    /// <summary>Raised when an accepted peer's read loop ends.</summary>
    public event Action<RpcPeer>? PeerDisconnected;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(RpcHost));
        }

        if (_cts is not null)
        {
            throw new InvalidOperationException("Host is already running.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _listener.StartAsync(ct).ConfigureAwait(false);
        _acceptTask = AcceptLoopAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();

        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _listener.StopAsync(ct).ConfigureAwait(false);
        await ClosePeersAsync().ConfigureAwait(false);
        _cts.Dispose();
        _cts = null;
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
            catch
            {
                continue;
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
        PeerConnected?.Invoke(peer);
    }

    private void OnPeerDisconnected(object? sender, RpcDisconnectedEventArgs args)
    {
        if (sender is not RpcPeer peer)
        {
            return;
        }

        _peers.TryRemove(peer, out _);
        PeerDisconnected?.Invoke(peer);

        // Dispose off the read-loop callback so DisposeAsync can await the now-completing loop
        // without deadlocking on itself.
        _ = Task.Run(async () =>
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
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        await ClosePeersAsync().ConfigureAwait(false);
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
}

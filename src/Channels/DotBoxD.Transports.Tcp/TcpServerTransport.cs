using System.Net;
using System.Net.Sockets;
using DotBoxD.Services.Transport;
namespace DotBoxD.Transports.Tcp;
/// <summary>
/// TCP server transport implementation.
/// </summary>
public sealed class TcpServerTransport : IServerTransport
{
    private readonly IPAddress _address;
    private readonly int _port;
    private readonly SemaphoreSlim _acceptLock = new(1, 1);
    private readonly object _lifecycleLock = new();
    private TcpListener? _listener;
    private Task<TcpClient>? _pendingAccept;
    private int _disposed;
    private int _started;
    private int _freshAcceptStartsForTest;
    public TcpServerTransport(int port) : this(IPAddress.Any, port) { }
    public TcpServerTransport(IPAddress address, int port)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _port = EnsurePort(port);
    }
    public TcpServerTransport(string address, int port)
    {
        _address = ParseAddress(address);
        _port = EnsurePort(port);
    }
    private static IPAddress ParseAddress(string address)
    {
        if (address == null)
        {
            throw new ArgumentNullException(nameof(address));
        }

        return IPAddress.TryParse(address, out var parsed)
            ? parsed
            : throw new ArgumentException("Address must be a valid IP address.", nameof(address));
    }
    private static int EnsurePort(int port) => (uint)port <= 65535 ? port : throw new ArgumentOutOfRangeException(nameof(port));
    /// <summary>
    /// Gets the bound endpoint after <see cref="StartAsync"/> succeeds.
    /// </summary>
    public IPEndPoint? LocalEndpoint => _listener?.LocalEndpoint as IPEndPoint;
    /// <summary>
    /// Idle timeout applied to accepted connections' frame reads (slow-loris
    /// defense). <see langword="null"/> uses <see cref="TcpConnection.DefaultFrameReadIdleTimeout"/>;
    /// <see cref="Timeout.InfiniteTimeSpan"/> disables it. See <see cref="TcpConnection"/>.
    /// </summary>
    public TimeSpan? FrameReadIdleTimeout { get; init; }
    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lifecycleLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(TcpServerTransport));
            }
            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                throw new InvalidOperationException("Server already started.");
            }
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(_address, _port);
                listener.Start();
                _onListenerStartedBeforePublishForTest?.Invoke();
                // The test seam can reenter shutdown while startup holds the lifecycle lock.
                if (Volatile.Read(ref _disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(TcpServerTransport));
                }
                if (Volatile.Read(ref _started) == 0)
                {
                    throw new OperationCanceledException("Server startup was stopped.", ct);
                }
                _listener = listener;
            }
            catch
            {
                Volatile.Write(ref _started, 0);
                listener?.Stop();
                throw;
            }
        }
        return Task.CompletedTask;
    }
    public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpServerTransport));
        }
        // Honour an already-cancelled token before claiming or starting any accept, so a pre-cancelled
        // call neither consumes a stashed accept nor starts (and then orphans) a fresh
        // listener.AcceptTcpClientAsync() that the shutdown observation path could never reclaim.
        ct.ThrowIfCancellationRequested();
        await _acceptLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await AcceptCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _acceptLock.Release();
        }
    }
    private async Task<IRpcChannel> AcceptCoreAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpServerTransport));
        }
        ct.ThrowIfCancellationRequested();
        // Capture the listener once: a concurrent StopAsync/DisposeAsync nulls the field, and reading
        // it twice could NRE between the guard and the accept call. If Stop races in after this read,
        // AcceptTcpClientAsync simply faults on the stopped listener and the catch below maps it.
        var listener = _listener;
        if (listener == null)
        {
            throw new InvalidOperationException("Server not started.");
        }
        // netstandard2.1 has no CancellationToken overload for AcceptTcpClientAsync, and Stop()-ing
        // the listener to unblock would tear it down for every future accept. Instead race the accept
        // against the token; on cancellation keep the in-flight accept to hand back on the next call
        // so the listener stays alive. AcceptAsync serializes OS-level accepts because _pendingAccept
        // tracks only one cancelled in-flight accept for later reuse or shutdown cleanup. StopAsync/
        // DisposeAsync (any thread) reclaim _pendingAccept via Interlocked, so consume it atomically
        // here too -- a plain read+null could let both this call and ObservePendingAccept take the same
        // stashed accept, returning a TcpClient that is also disposed at shutdown.
        var acceptTask = StartOrClaimAccept(listener);
        // Honour an already-cancelled token before the IsCompleted short-circuit below can return a
        // completed (e.g. stashed) accept. If the accept came from the stash, re-stash it first so the
        // in-flight accept (and any socket it completes with) is reclaimed by the shutdown observation
        // path instead of being leaked, mirroring the cancellation re-stash logic below.
        if (ct.IsCancellationRequested)
        {
            RestashAcceptForCancellation(acceptTask);
            throw new OperationCanceledException(ct);
        }
        await WaitForAcceptOrCancellationAsync(acceptTask, ct).ConfigureAwait(false);
        var client = await CompleteAcceptAsync(acceptTask, ct).ConfigureAwait(false);
        try
        {
            return new TcpConnection(client, FrameReadIdleTimeout);
        }
        catch
        {
            // The OS socket was already accepted; if TcpConnection construction fails (e.g. an invalid
            // FrameReadIdleTimeout), dispose the client so its socket is not leaked — otherwise the host
            // accept loop's error-retry cycle would leak one socket per iteration. Mirrors the equivalent
            // catch in NamedPipeServerTransport.AcceptAsync.
            client.Dispose();
            throw;
        }
    }

    private Task<TcpClient> StartOrClaimAccept(TcpListener listener)
    {
        if (ClaimPendingAccept() is { } claimed)
        {
            return claimed;
        }

        // Count fresh OS-level accepts so a deterministic test can prove that a pre-cancelled token
        // does not start (and orphan) one. Inert in production beyond a single Interlocked increment.
        Interlocked.Increment(ref _freshAcceptStartsForTest);
        var acceptTask = listener.AcceptTcpClientAsync();
        // Fire the fresh-accept seam (null/no-op in production) so a deterministic test can race a
        // concurrent cancellation into the window between starting this fresh accept and the in-body
        // IsCancellationRequested check below.
        _onFreshAcceptStartedForTest?.Invoke();
        return acceptTask;
    }

    private async Task WaitForAcceptOrCancellationAsync(Task<TcpClient> acceptTask, CancellationToken ct)
    {
        if (!ct.CanBeCanceled || acceptTask.IsCompleted)
        {
            return;
        }

        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancelled);
        var completed = await Task.WhenAny(acceptTask, cancelled.Task).ConfigureAwait(false);
        if (completed == acceptTask)
        {
            return;
        }

        RestashAcceptForCancellation(acceptTask);
        throw new OperationCanceledException(ct);
    }

    private void RestashAcceptForCancellation(Task<TcpClient> acceptTask)
    {
        // Re-stash whatever accept we hold — a claimed one OR a freshly-started one — so the
        // in-flight accept (and any socket it completes with) is reclaimed by the shutdown
        // observation path instead of being orphaned.
        _ = Interlocked.Exchange(ref _pendingAccept, acceptTask);
        if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _disposed) != 0)
        {
            ObservePendingAccept();
        }
    }

    private async Task<TcpClient> CompleteAcceptAsync(Task<TcpClient> acceptTask, CancellationToken ct)
    {
        try
        {
            return await acceptTask.ConfigureAwait(false);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (Exception) when (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _disposed) != 0)
        {
            throw new OperationCanceledException();
        }
    }
    public Task StopAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lifecycleLock)
        {
            Volatile.Write(ref _started, 0);
            var listener = Interlocked.Exchange(ref _listener, null);
            listener?.Stop();
            ObservePendingAccept();
        }
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return default;
            }
            Volatile.Write(ref _started, 0);
            var listener = Interlocked.Exchange(ref _listener, null);
            listener?.Stop();
            ObservePendingAccept();
        }
        return default;
    }
    private void ObservePendingAccept()
        => TcpPendingAcceptObserver.Observe(Interlocked.Exchange(ref _pendingAccept, null));
    /// <summary>
    /// Atomically claims any stashed in-flight accept. Reads the field, fires the test seam (a no-op in
    /// production), then claims the stashed task with a <see cref="Interlocked.CompareExchange{T}"/> —
    /// if a concurrent <see cref="ObservePendingAccept"/> (Stop/Dispose) reclaimed it in between, the CAS
    /// fails and this returns <see langword="null"/> so the caller starts a fresh accept instead of
    /// double-taking the same <see cref="TcpClient"/> that shutdown is disposing.
    /// </summary>
    private Task<TcpClient>? ClaimPendingAccept()
    {
        var stashed = Volatile.Read(ref _pendingAccept);
        _onPendingAcceptConsumeForTest?.Invoke();
        if (stashed is not null && Interlocked.CompareExchange(ref _pendingAccept, null, stashed) == stashed)
        {
            return stashed;
        }
        return null;
    }
    // --- Test seams (null/no-op in production) for the deterministic _pendingAccept double-consume test ---
    /// <summary>Invoked inside <see cref="ClaimPendingAccept"/> between reading and claiming the stash,
    /// so a test can deterministically race a concurrent reclaim into that window.</summary>
    internal Action? _onPendingAcceptConsumeForTest;
    /// <summary>Invoked inside <see cref="AcceptAsync"/> right after a fresh
    /// <c>listener.AcceptTcpClientAsync()</c> is started (and before the in-body cancellation check),
    /// so a test can deterministically cancel the token in that exact window. No-op in production.</summary>
    internal Action? _onFreshAcceptStartedForTest;

    /// <summary>Invoked inside <see cref="StartAsync"/> after <c>listener.Start()</c> but before the
    /// listener is published to <c>_listener</c>, so a test can deterministically race a concurrent
    /// <see cref="DisposeAsync"/> into the publish window. No-op in production.</summary>
    internal Action? _onListenerStartedBeforePublishForTest;

    internal Task<TcpClient>? ClaimPendingAcceptForTest() => ClaimPendingAccept();

    internal void StashPendingAcceptForTest(Task<TcpClient> accept) => Volatile.Write(ref _pendingAccept, accept);

    internal Task<TcpClient>? ReclaimPendingAcceptForTest() => Interlocked.Exchange(ref _pendingAccept, null);

    /// <summary>Number of fresh OS-level accepts started (i.e. not served from the stash). A
    /// pre-cancelled <see cref="AcceptAsync"/> must not start one.</summary>
    internal int FreshAcceptStartsForTest => Volatile.Read(ref _freshAcceptStartsForTest);
}

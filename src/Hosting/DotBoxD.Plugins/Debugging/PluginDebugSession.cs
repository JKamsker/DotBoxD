using System.Security.Cryptography;
using System.Text.Json;
using DotBoxD.Kernels.Debugging;

namespace DotBoxD.Plugins.Debugging;

/// <summary>
/// Authenticated, session-owned version-one remote-debug endpoint. Dispose it when the plugin connection drops.
/// </summary>
public sealed class PluginDebugSession : IPluginDebugControlEndpoint, IDisposable, IAsyncDisposable
{
    private const int TokenByteCount = 32;
    private readonly object _gate = new();
    private readonly PluginDebugCoordinator _coordinator;
    private readonly IPluginDebugEventEndpoint _events;
    private readonly PluginDebugRequestHandler _requests;
    private readonly byte[] _tokenBytes = RandomNumberGenerator.GetBytes(TokenByteCount);
    private readonly Timer _leaseTimer;
    private CancellationTokenSource _attachmentCancellation = new();
    private bool _attached;
    private bool _disposed;
    private KernelDebugPauseScope _pauseScope;
    private long _nextEventId;

    internal PluginDebugSession(
        PluginDebugCoordinator coordinator,
        PluginSession owner,
        IPluginDebugEventEndpoint events)
    {
        _coordinator = coordinator;
        Owner = owner;
        _events = events;
        SessionToken = Convert.ToHexString(_tokenBytes).ToLowerInvariant();
        _leaseTimer = new Timer(static state => ((PluginDebugSession)state!).OnLeaseExpired(), this, -1, -1);
        _requests = new PluginDebugRequestHandler(this);
        ExecutionState = new PluginDebugExecutionState();
        Assemblies = new PluginDebugAssemblyStore(Options.MaxAssemblyUploadBytes);
    }

    /// <summary>Authentication token that the plugin-side bridge must put in every envelope.</summary>
    public string SessionToken { get; }

    /// <summary>Whether this endpoint currently owns the server's single debugger slot.</summary>
    public bool IsAttached
    {
        get
        {
            lock (_gate)
            {
                return _attached;
            }
        }
    }

    internal PluginSession Owner { get; }

    internal PluginRemoteDebugOptions Options => _coordinator.Options;

    internal PluginDebugExecutionState ExecutionState { get; }

    internal PluginDebugAssemblyStore Assemblies { get; }

    internal IReadOnlySet<KernelDebugPauseScope> AllowedPauseScopes => _coordinator.AllowedPauseScopes;

    internal KernelDebugPauseScope PauseScope
    {
        get
        {
            lock (_gate)
            {
                return _pauseScope;
            }
        }
    }

    public ValueTask<byte[]> ExchangeAsync(byte[] message, CancellationToken cancellationToken = default)
        => _requests.ExchangeAsync(message, cancellationToken);

    public void Dispose()
    {
        bool detach;
        CancellationTokenSource attachmentCancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            detach = _attached;
            _attached = false;
            attachmentCancellation = _attachmentCancellation;
            _leaseTimer.Change(-1, -1);
        }

        attachmentCancellation.Cancel();
        Assemblies.Clear();
        if (detach)
        {
            _coordinator.Detach(this);
        }

        _leaseTimer.Dispose();
        attachmentCancellation.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal void DetachFromServer() => DetachCore();

    internal bool TryAttach(KernelDebugPauseScope pauseScope)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_attached || !_coordinator.TryAttach(this, pauseScope))
            {
                return false;
            }

            _pauseScope = pauseScope;
            _attached = true;
            _attachmentCancellation.Dispose();
            _attachmentCancellation = new CancellationTokenSource();
            _leaseTimer.Change(Options.StopLease, Timeout.InfiniteTimeSpan);
            return true;
        }
    }

    internal void RenewLease()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_attached)
            {
                _leaseTimer.Change(Options.StopLease, Timeout.InfiniteTimeSpan);
            }
        }
    }

    internal void Authenticate(string token)
    {
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(token);
        }
        catch (FormatException)
        {
            throw Unauthorized();
        }

        if (supplied.Length != TokenByteCount ||
            !CryptographicOperations.FixedTimeEquals(_tokenBytes, supplied))
        {
            throw Unauthorized();
        }
    }

    internal void DetachFromClient() => DetachCore();

    internal bool Resume(string runId) => _coordinator.Resume(this, runId);

    internal bool IsBreakpointVerified(string pluginId, SandboxNodeId nodeId)
        => _coordinator.IsBreakpointVerified(this, pluginId, nodeId);

    internal KernelDebugInfo? DebugInfo(string pluginId) => _coordinator.DebugInfo(this, pluginId);

    internal async ValueTask PublishEventAsync(
        string kind,
        object payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(payload);
        string id;
        CancellationToken attachmentCancellation;
        lock (_gate)
        {
            if (!_attached || _disposed)
            {
                return;
            }

            id = Interlocked.Increment(ref _nextEventId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            attachmentCancellation = _attachmentCancellation.Token;
        }

        var envelope = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            kind,
            id,
            SessionToken,
            JsonSerializer.SerializeToElement(payload));
        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                attachmentCancellation);
            await _events.PublishAsync(
                    PluginDebugProtocol.Encode(envelope, Options.MaxMessageBytes),
                    linkedCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            attachmentCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Detach/lease expiry is the successful cleanup path for an in-flight reverse publication.
        }
        catch
        {
            DetachCore();
            throw;
        }
    }

    /// <summary>
    /// Publishes the authenticated session bootstrap over the configured reverse endpoint. Connection helpers
    /// call this after both duplex services have been provisioned; manual transports may do the same.
    /// </summary>
    public async ValueTask PublishBootstrapAsync(CancellationToken cancellationToken = default)
    {
        var envelope = new PluginDebugEnvelope(
            PluginDebugProtocol.Version,
            "session",
            "bootstrap",
            SessionToken,
            JsonSerializer.SerializeToElement(new
            {
                protocolVersion = PluginDebugProtocol.Version,
                sessionToken = SessionToken
            }));
        try
        {
            await _events.PublishAsync(
                    PluginDebugProtocol.Encode(envelope, Options.MaxMessageBytes),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            DetachCore();
            throw;
        }
    }

    private void OnLeaseExpired() => DetachCore();

    private static PluginDebugProtocolException Unauthorized()
        => new("unauthorized", "The debug session token is invalid for this plugin session.");

    private void DetachCore()
    {
        bool detach;
        CancellationTokenSource? attachmentCancellation = null;
        lock (_gate)
        {
            detach = _attached;
            _attached = false;
            if (detach)
            {
                attachmentCancellation = _attachmentCancellation;
            }

            _leaseTimer.Change(-1, -1);
        }

        attachmentCancellation?.Cancel();
        Assemblies.Clear();
        if (detach)
        {
            _coordinator.Detach(this);
        }
    }
}

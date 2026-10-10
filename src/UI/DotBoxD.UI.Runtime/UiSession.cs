using System.Collections.Immutable;

namespace DotBoxD.UI.Runtime;

/// <summary>Host-authoritative, serialized state with optimistic remote patches and cancellable teardown.</summary>
public sealed class UiSession : IAsyncDisposable
{
    private readonly UiPolicy _policy;
    private readonly UiRendererOwner _renderer;
    private IUiRemoteTransport? _remote;
    private readonly UiStateStore _state;
    private readonly UiBindings _bindings;
    private readonly UiKernelRunner _kernels;
    private readonly UiInteractionState _interaction;
    private readonly Dictionary<int, UiEvent> _events;
    private readonly Dictionary<(int Node, UiPropertyId Property), int> _inputs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _disposeLock = new();
    private Task? _disposeTask;
    private int _closed;
    private int _inFlight;

    internal UiSession(
        UiPackage package, UiPolicy policy, UiRendererOwner renderer, IUiRemoteTransport? remote,
        UiStateStore state, UiBindings bindings, UiKernelRunner kernels)
    {
        _policy = policy;
        _renderer = renderer;
        _remote = remote;
        _state = state;
        _bindings = bindings;
        _kernels = kernels;
        _interaction = new UiInteractionState(package);
        _interaction.Commit(bindings.Current);
        _events = package.Events.ToDictionary(e => e.Id);
        _inputs = package.Nodes.SelectMany(n => n.Properties.Where(p => p.TwoWay)
            .Select(p => ((n.Id, p.Id), p.StateSlotId))).ToDictionary(p => p.Item1, p => p.StateSlotId);
    }

    internal void StartInput(IUiInputSource source)
        => _ = UiInputLoop.RunAsync(this, source, _policy,
            _events.Values.Where(e => e.Target == UiEventTarget.Remote).Select(e => e.Id).ToHashSet(), _lifetime.Token);

    private string? _lastInputError;
    public string? LastInputError => Volatile.Read(ref _lastInputError);
    internal void RecordInputError(string message) => Volatile.Write(ref _lastInputError, message);

    internal ValueTask<bool> ProcessInputAsync(UiInput input, IUiInputSource source, CancellationToken token)
        => LockedAsync(async () =>
        {
            string? error = null;
            try
            { await SetInputCoreAsync(input.NodeId, input.PropertyId, input.Value!, token).ConfigureAwait(false); }
            catch (UiValidationException rejected)
            {
                error = rejected.Message;
                if (_inputs.ContainsKey((input.NodeId, input.PropertyId)))
                { await _renderer.UpdateAsync([_bindings.GetCurrent(input.NodeId, input.PropertyId)], token).ConfigureAwait(false); }
            }
            await UiRendererOwner.AcknowledgeAsync(source, input, token).ConfigureAwait(false);
            if (error is not null)
            { RecordInputError(error); }
            return true;
        }, token);

    public Guid Id { get; } = Guid.NewGuid();
    /// <summary>Trusted presentation adapter; null once teardown releases it.</summary>
    public IUiRenderer? Renderer => _renderer.Renderer;
    public bool IsDisconnected => Volatile.Read(ref _closed) != 0;

    public ValueTask<UiSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
        => LockedAsync(() => ValueTask.FromResult(_state.Snapshot(Id)), cancellationToken);

    public ValueTask<UiSnapshot> ApplyPatchAsync(UiStatePatch patch, CancellationToken cancellationToken = default)
        => LockedAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(patch);
            if (patch.SessionId != Id || patch.ExpectedVersion != _state.Version)
            {
                throw new UiValidationException("UI patch session/version is stale or belongs to another session.");
            }

            return await CommitAsync(patch.Writes, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public ValueTask<UiSnapshot> SetInputAsync(
        int nodeId, UiPropertyId propertyId, UiValue value, CancellationToken cancellationToken = default)
        => LockedAsync(() => SetInputCoreAsync(nodeId, propertyId, value, cancellationToken), cancellationToken);

    private ValueTask<UiSnapshot> SetInputCoreAsync(int nodeId, UiPropertyId propertyId, UiValue value, CancellationToken token)
    {
        if (!_inputs.TryGetValue((nodeId, propertyId), out var slot))
        {
            throw new UiValidationException("Input must address a declared two-way property.");
        }
        _interaction.Validate(nodeId);
        return CommitAsync([new UiStateValue(slot, value)], token);
    }

    /// <summary>
    /// Executes a declared local/remote event. Remote transport faults and host deadlines disconnect
    /// the session; caller cancellation cancels only that dispatch. Teardown invalidates late replies.
    /// </summary>
    public async ValueTask<UiSnapshot> DispatchAsync(int eventId, CancellationToken cancellationToken = default)
    {
        var dispatch = await LockedAsync(async () =>
        {
            if (!_events.TryGetValue(eventId, out var route))
            {
                throw new UiValidationException("Unknown UI event ID.");
            }

            if (route.Target == UiEventTarget.LocalKernel)
            {
                _interaction.Validate(route.NodeId);
                using var local = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
                var result = await _kernels.ExecuteAsync(route.KernelId, _state.Values, local.Token).ConfigureAwait(false);
                var snapshot = await CommitAsync([new UiStateValue(route.OutputSlotId, result)], local.Token).ConfigureAwait(false);
                return new Dispatch(snapshot, null, null, null);
            }

            if (Volatile.Read(ref _inFlight) >= _policy.MaxInFlightRemoteEvents)
            {
                throw new UiValidationException("UI remote event concurrency limit exceeded.");
            }
            _interaction.Validate(route.NodeId);

            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            linked.CancelAfter(_policy.RemoteEventTimeout);
            Interlocked.Increment(ref _inFlight);
            return new Dispatch(null, new UiRemoteEvent(route.Id, route.NodeId, route.RemoteEndpointId, _state.Snapshot(Id)),
                _remote!, linked);
        }, cancellationToken).ConfigureAwait(false);
        if (dispatch.Local is { } localSnapshot)
        {
            return localSnapshot;
        }

        using var pending = dispatch.Cancellation!;
        try
        {
            var request = dispatch.Remote!;
            var patch = await CallRemoteAsync(dispatch.Transport!, request, pending.Token, cancellationToken).ConfigureAwait(false);
            if (patch is null || patch.SessionId != request.Snapshot.SessionId ||
                patch.ExpectedVersion != request.Snapshot.Version)
            {
                throw new UiValidationException("Remote reply must match the dispatched session and snapshot version.");
            }

            return await ApplyPatchAsync(patch, pending.Token).ConfigureAwait(false);
        }
        catch when (!cancellationToken.IsCancellationRequested && pending.IsCancellationRequested)
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private async ValueTask<UiStatePatch> CallRemoteAsync(
        IUiRemoteTransport transport, UiRemoteEvent message, CancellationToken token, CancellationToken caller)
    {
        try
        {
            return await transport.DispatchAsync(message, token).AsTask().WaitAsync(token).ConfigureAwait(false);
        }
        catch when (caller.IsCancellationRequested)
        {
            caller.ThrowIfCancellationRequested();
            throw;
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<UiSnapshot> CommitAsync(ImmutableArray<UiStateValue> writes, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var next = _state.Stage(writes);
        var values = await _bindings.EvaluateAsync(next, linked.Token).ConfigureAwait(false);
        ThrowIfDisconnected();
        linked.Token.ThrowIfCancellationRequested();
        if (writes.Length == 0)
        {
            return _state.Snapshot(Id);
        }

        var changes = _bindings.Changes(values);
        _state.Commit(next);
        _bindings.Commit(values);
        _interaction.Commit(values);
        if (!changes.IsEmpty)
        {
            await _renderer.UpdateAsync(changes, linked.Token).ConfigureAwait(false);
        }

        ThrowIfDisconnected();
        return _state.Snapshot(Id);
    }

    private async ValueTask<T> LockedAsync<T>(Func<ValueTask<T>> action, CancellationToken token)
    {
        try
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                ThrowIfDisconnected();
                return await action().ConfigureAwait(false);
            }
            catch (UiRendererException)
            {
                Interlocked.Exchange(ref _closed, 1);
                throw;
            }
            finally { _gate.Release(); }
        }
        catch (UiRendererException)
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _closed, 1);
        try
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            await ReleaseResourcesAsync().ConfigureAwait(false);
        }
    }

    private async Task ReleaseResourcesAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _remote = null;
            _events.Clear();
            _inputs.Clear();
            _state.Clear();
            _bindings.Clear();
            _interaction.Clear();
            _kernels.Clear();
            await _renderer.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _lifetime.Dispose();
        }
    }

    private void ThrowIfDisconnected() => ObjectDisposedException.ThrowIf(IsDisconnected, this);

    private sealed record Dispatch(
        UiSnapshot? Local, UiRemoteEvent? Remote, IUiRemoteTransport? Transport, CancellationTokenSource? Cancellation);
}

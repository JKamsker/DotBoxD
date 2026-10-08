using System.Collections.Immutable;
using System.Security.Claims;
using System.Threading.Channels;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor;

/// <summary>
/// One validated installation and one viewer. Trusted components subscribe to immutable snapshots;
/// browser inputs are authorized and queued without reentering the session from renderer callbacks.
/// </summary>
public sealed class BlazorUiRenderer : IUiRenderer, IUiInputSource
{
    private readonly object _sync = new();
    private readonly Channel<UiInput> _inputs;
    private readonly UiPolicy _inputPolicy;
    private readonly BlazorInputAdmission _admission;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<(int Node, UiPropertyId Property), UiInput> _latest = [];
    private BlazorUiSnapshot? _snapshot;
    private Func<Task>? _changed;
    private bool _disposed;
    private bool _installed;

    public BlazorUiRenderer(UiPolicy? inputPolicy = null, int inputCapacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputCapacity);
        _inputPolicy = inputPolicy ?? new UiPolicy();
        _inputPolicy.Validate();
        _admission = new BlazorInputAdmission(_inputPolicy);
        _inputs = Channel.CreateBounded<UiInput>(new BoundedChannelOptions(inputCapacity)
        { FullMode = BoundedChannelFullMode.Wait });
    }

    public UiRendererCapabilities Capabilities => UiRendererCapabilities.Core;
    public BlazorUiSnapshot? Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot is { } snapshot && _latest.Count > 0
                    ? snapshot with
                    {
                        Values = snapshot.Values.SetItems(_latest.Select(p =>
                        new KeyValuePair<(int, UiPropertyId), UiValue>(p.Key, p.Value.Value!)))
                    } : _snapshot;
            }
        }
    }
    public bool IsDisposed { get { lock (_sync) { return _disposed; } } }
    public int Materializations { get; private set; }
    public int Updates { get; private set; }

    /// <summary>Subscribe one trusted viewer. Disposing the subscription detaches its circuit reference.</summary>
    public IDisposable Subscribe(Func<Task> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_changed is not null)
            { throw new InvalidOperationException("Create a separate UiSession for each Blazor viewer."); }
            _changed = changed;
            return new Subscription(this, changed);
        }
    }

    public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_installed)
            { throw new InvalidOperationException("A Blazor renderer owns one UI installation."); }
            _installed = true;
            _snapshot = new BlazorUiSnapshot(package, package.Nodes.ToImmutableDictionary(n => n.Id),
                values.ToImmutableDictionary(v => (v.NodeId, v.PropertyId), v => v.Value));
            Materializations++;
        }
        return ValueTask.CompletedTask;
    }

    public async ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Func<Task>? changed;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var snapshot = _snapshot ?? throw new InvalidOperationException("UI is not installed.");
            _snapshot = snapshot with
            {
                Values = snapshot.Values.SetItems(changes.Select(v =>
                new KeyValuePair<(int, UiPropertyId), UiValue>((v.NodeId, v.PropertyId), v.Value)))
            };
            Updates++;
            changed = _changed;
        }
        if (changed is not null)
        { await changed().ConfigureAwait(false); }
    }

    /// <summary>
    /// Authorizes input from a trusted host principal. The session object binds callbacks to their
    /// installation; IDs supplied by a browser are never used to look up another viewer's session.
    /// Returns queue admission, not completion of the semantic operation.
    /// </summary>
    public async ValueTask<bool> SubmitAsync(UiSession session, UiInput input, ClaimsPrincipal user,
        IUiInteractionAuthorizer authorizer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(authorizer);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(session.Renderer, this) || session.IsDisconnected)
        { throw new UiValidationException("Browser input belongs to another or disconnected UI session."); }
        CancellationTokenSource linked;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            BlazorInputAdmission.Validate(_snapshot!, input, _inputPolicy);
            _admission.Begin();
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        }
        using var cancellation = linked;
        try
        {
            if (!await authorizer.AuthorizeAsync(user, session, input, linked.Token).AsTask().WaitAsync(linked.Token).ConfigureAwait(false))
            { return false; }
            linked.Token.ThrowIfCancellationRequested();
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                BlazorInputAdmission.Validate(_snapshot!, input, _inputPolicy);
                if (_inputs.Writer.TryWrite(input))
                {
                    if (input.NodeId > 0)
                    { _latest[(input.NodeId, input.PropertyId)] = input; }
                    return true;
                }
                _inputs.Writer.TryComplete(new UiValidationException("Blazor UI input queue limit exceeded."));
                throw new UiValidationException("Blazor UI input queue limit exceeded.");
            }
        }
        finally { _admission.End(); }
    }

    public ValueTask<UiInput> ReadAsync(CancellationToken cancellationToken) => _inputs.Reader.ReadAsync(cancellationToken);

    public async ValueTask AcknowledgeAsync(UiInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Func<Task>? changed = null;
        lock (_sync)
        {
            var key = (input.NodeId, input.PropertyId);
            if (_latest.TryGetValue(key, out var latest) && ReferenceEquals(latest, input))
            { _latest.Remove(key); changed = _changed; }
        }
        if (changed is not null)
        { await changed().ConfigureAwait(false); }
    }

    public async ValueTask DisposeAsync()
    {
        Func<Task>? changed;
        lock (_sync)
        {
            if (_disposed)
            { return; }
            _disposed = true;
            _snapshot = null;
            _latest.Clear();
            changed = _changed;
            _changed = null;
            _inputs.Writer.TryComplete();
            while (_inputs.Reader.TryRead(out _))
            { }
        }
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _lifetime.Dispose();
        if (changed is not null)
        { await changed().ConfigureAwait(false); }
    }

    private sealed class Subscription(BlazorUiRenderer renderer, Func<Task> changed) : IDisposable
    {
        public void Dispose()
        {
            lock (renderer._sync)
            { if (renderer._changed == changed) { renderer._changed = null; } }
        }
    }
}

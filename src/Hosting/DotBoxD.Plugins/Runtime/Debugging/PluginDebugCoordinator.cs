namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginDebugCoordinator : IDisposable
{
    private readonly object _gate = new();
    private PluginDebugSession? _attached;
    private bool _disposed;

    public PluginDebugCoordinator(PluginRemoteDebugOptions? options)
    {
        options ??= new PluginRemoteDebugOptions();
        options.Validate();
        Options = options with { AllowedPauseScopes = options.SnapshotAllowedPauseScopes().ToArray() };
        AllowedPauseScopes = Options.AllowedPauseScopes.ToHashSet();
    }

    public PluginRemoteDebugOptions Options { get; }

    public IReadOnlySet<KernelDebugPauseScope> AllowedPauseScopes { get; }

    public bool TryAttach(PluginDebugSession session)
    {
        lock (_gate)
        {
            if (_disposed || !Options.Enabled || (_attached is not null && !ReferenceEquals(_attached, session)))
            {
                return false;
            }

            _attached = session;
            return true;
        }
    }

    public void Detach(PluginDebugSession session)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_attached, session))
            {
                _attached = null;
            }
        }
    }

    public void Dispose()
    {
        PluginDebugSession? attached;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            attached = _attached;
            _attached = null;
        }

        attached?.DetachFromServer();
    }
}

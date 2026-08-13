using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Plugins;

public sealed partial class PluginSession
{
    private PluginDebugSession? _debugSession;

    /// <summary>
    /// Creates the authenticated debug endpoint owned by this plugin connection. Only one endpoint may be
    /// created per plugin session, and disposing the owner disposes it automatically.
    /// </summary>
    public PluginDebugSession CreateDebugSession(IPluginDebugEventEndpoint eventEndpoint)
    {
        ArgumentNullException.ThrowIfNull(eventEndpoint);
        _gate.Wait();
        try
        {
            ThrowIfDisposed();
            if (_debugSession is not null)
            {
                throw new InvalidOperationException("This plugin session already owns a debug endpoint.");
            }

            _debugSession = _server.CreateDebugSession(this, eventEndpoint);
            return _debugSession;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void DisposeDebugSession()
    {
        _debugSession?.Dispose();
        _debugSession = null;
    }
}

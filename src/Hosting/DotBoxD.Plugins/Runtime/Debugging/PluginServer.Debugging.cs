using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Plugins;

public sealed partial class PluginServer
{
    private readonly PluginDebugCoordinator _debugCoordinator;

    internal PluginDebugSession CreateDebugSession(
        PluginSession owner,
        IPluginDebugEventEndpoint eventEndpoint)
    {
        ThrowIfDisposed();
        return new PluginDebugSession(_debugCoordinator, owner, eventEndpoint);
    }
}

using DotBoxD.Plugins;

namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Client-side fire-and-forget subscription registration surface for a remote plugin server.
/// </summary>
public sealed class RemoteSubscriptionRegistry
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;
    private readonly Func<RemoteHostCallbackRegistration, ValueTask<string>>? _installHostCallback;

    public RemoteSubscriptionRegistry(
        Func<PluginPackage, ValueTask<string>> install,
        Func<RemoteHostCallbackRegistration, ValueTask<string>>? installHostCallback = null)
    {
        _install = install ?? throw new ArgumentNullException(nameof(install));
        _installHostCallback = installHostCallback;
    }

    public RemoteSubscriptionPipeline<TEvent> On<TEvent>() => new(_install, _installHostCallback);
}

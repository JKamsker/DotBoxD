using DotBoxD.Plugins;

namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Client-side hook registration surface for a remote plugin server. The fluent stages are lowered by
/// the analyzer; the terminal installs the generated package through the supplied control-plane callback.
/// </summary>
public sealed class RemoteHookRegistry
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;

    public RemoteHookRegistry(Func<PluginPackage, ValueTask<string>> install)
        => _install = install ?? throw new ArgumentNullException(nameof(install));

    public RemoteHookPipeline<TEvent> On<TEvent>() => new(_install);
}

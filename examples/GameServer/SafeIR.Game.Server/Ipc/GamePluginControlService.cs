namespace SafeIR.Game.Server;

/// <summary>
/// Implements the IPC control plane over the running <see cref="PluginServer"/> and
/// <see cref="GameWorld"/>. The server installs untrusted plugins as opaque verified IR
/// (<see cref="PluginServerJsonExtensions.InstallJsonAsync"/>) — it never sees kernel source — and
/// wires the hook for whichever event the installed kernel subscribes to.
/// </summary>
internal sealed class GamePluginControlService : IGamePluginControlService
{
    private readonly PluginServer _server;
    private readonly GameCommandSink _sink;
    private readonly GameWorld _world;

    public GamePluginControlService(PluginServer server, GameCommandSink sink, GameWorld world)
    {
        _server = server;
        _sink = sink;
        _world = world;
    }

    public async ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(packageJson);
        var kernel = await _server.InstallJsonAsync(packageJson, cancellationToken: ct).ConfigureAwait(false);
        WireHook(kernel);
        return kernel.Manifest.PluginId;
    }

    public ValueTask UpdateSettingsAsync(
        string pluginId,
        LiveSettingUpdate[] updates,
        bool atomic = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(updates);
        var values = new Dictionary<string, object?>(updates.Length, StringComparer.Ordinal);
        foreach (var update in updates)
        {
            values[update.Name] = update.Value;
        }

        return _server.Kernels.Get(pluginId).ModifySettingsAsync(values, atomic, ct);
    }

    public ValueTask<WorldSnapshot> GetWorldAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_world.Snapshot());
    }

    public ValueTask<string[]> DrainEffectsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_sink.DrainEffects());
    }

    private void WireHook(InstalledKernel kernel)
    {
        // Map by the kernel's declared subscription event so the server stays agnostic of plugin ids.
        var subscription = kernel.Manifest.Subscriptions.Count > 0
            ? kernel.Manifest.Subscriptions[0].Event
            : null;
        switch (subscription)
        {
            case "MonsterAggroEvent":
                _server.Hooks.On(MonsterAggroEventAdapter.Instance).UseKernel(kernel);
                break;
            case "AttackEvent":
                _server.Hooks.On(AttackEventAdapter.Instance).UseKernel(kernel);
                break;
            default:
                throw new InvalidOperationException(
                    $"Plugin '{kernel.Manifest.PluginId}' subscribes to unsupported event '{subscription}'.");
        }
    }
}

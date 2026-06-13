namespace SafeIR.Game.Server.Abstractions;

using ShaRPC.Core.Attributes;

/// <summary>
/// Control plane the plugin host calls over IPC. The host ships opaque verified IR
/// (<see cref="InstallPluginAsync"/>), tunes live settings (<see cref="UpdateSettingsAsync"/>), and
/// can observe the running simulation (<see cref="GetWorldAsync"/>, <see cref="DrainEffectsAsync"/>).
/// </summary>
[ShaRpcService]
public interface IGamePluginControlService
{
    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);

    ValueTask UpdateSettingsAsync(
        string pluginId,
        LiveSettingUpdate[] updates,
        bool atomic = false,
        CancellationToken ct = default);

    ValueTask<WorldSnapshot> GetWorldAsync(CancellationToken ct = default);

    ValueTask<string[]> DrainEffectsAsync(CancellationToken ct = default);
}

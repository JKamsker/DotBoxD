namespace SafeIR.Plugins;

using SafeIR;

public sealed record PluginManifest(
    string PluginId,
    string Contract,
    ExecutionMode Mode,
    IReadOnlyList<string> Effects,
    IReadOnlyList<LiveSettingDefinition> LiveSettings,
    IReadOnlyList<HookSubscriptionManifest> Subscriptions);

public sealed record LiveSettingDefinition(
    string Name,
    string Type,
    object? DefaultValue,
    object? Min = null,
    object? Max = null);

public sealed record HookSubscriptionManifest(string Event, string Kernel);

public sealed record KernelEntrypoints(string ShouldHandle, string Handle);

public sealed record PluginPackage(
    PluginManifest Manifest,
    SandboxModule Module,
    KernelEntrypoints Entrypoints)
{
    public static PluginPackage Create(
        PluginManifest manifest,
        SandboxModule module,
        KernelEntrypoints? entrypoints = null)
        => new(manifest, module, entrypoints ?? new KernelEntrypoints("ShouldHandle", "Handle"));
}

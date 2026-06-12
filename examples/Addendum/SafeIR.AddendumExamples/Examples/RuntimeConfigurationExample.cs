namespace SafeIR.AddendumExamples;

using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.Plugins;

internal static class RuntimeConfigurationExample
{
    public static async Task RunAsync()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages);
        var config = FireDamageConfiguration.Default with {
            Settings = new Dictionary<string, object?> {
                ["Enabled"] = true,
                ["DamageType"] = "ice",
                ["MinDamage"] = 250
            }
        };

        var installed = await server.InstallAsync(FireDamagePluginPackage.Create());
        await installed.ModifySettingsAsync(config.Settings);
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        await server.Hooks.PublishAsync(new DamageEvent("ice", 300, "player-1"));

        Console.WriteLine($"runtime config: mode={config.Mode}, messages={messages.Messages.Count}");
    }

    private sealed record FireDamageConfiguration(
        bool Enabled,
        string Mode,
        IReadOnlyDictionary<string, object?> Settings)
    {
        public static FireDamageConfiguration Default { get; } = new(
            true,
            "auto",
            new Dictionary<string, object?> {
                ["Enabled"] = true,
                ["DamageType"] = "fire",
                ["MinDamage"] = 100
            });
    }
}

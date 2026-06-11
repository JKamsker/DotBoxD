namespace SafeIR.AddendumExamples;

using SafeIR;
using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.Plugins;

internal static class ExecutionModeExample
{
    public static async Task RunAsync()
    {
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled, ExecutionMode.Auto }) {
            var messages = new InMemoryPluginMessageSink();
            var server = PluginServer.Create(messages);
            await server.InstallAsync(WithMode(FireDamagePluginPackage.Create(), mode));
            server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
            await server.Hooks.PublishAsync(new DamageEvent("fire", 120, $"player-{mode}"));
            Console.WriteLine($"execution mode {mode}: messages={messages.Messages.Count}");
        }
    }

    private static PluginPackage WithMode(PluginPackage package, ExecutionMode mode)
        => package with {
            Manifest = package.Manifest with { Mode = mode }
        };
}

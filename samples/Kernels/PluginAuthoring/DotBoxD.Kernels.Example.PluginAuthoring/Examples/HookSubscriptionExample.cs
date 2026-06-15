using DotBoxD.Kernels.PluginIpc.Server.Abstractions;

namespace DotBoxD.Kernels.Example.PluginAuthoring.Examples;

internal static class HookSubscriptionExample
{
    public static async Task RunAsync()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = Plugins.PluginServer.Create(messages, defaultPolicy: PluginExamplePolicies.MessageWrite());
        server.RegisterEventAdapter(DamageEventAdapter.Instance);
        var minDamage = server.BindValue("minDamage", 200);

        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>()
            .Where((e, _) => e.Amount >= minDamage.Value)
            .UseKernel<FireDamageKernel>();

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        await server.Hooks.PublishAsync(new DamageEvent("fire", 250, "player-2"));

        Console.WriteLine($"hook subscription: messages={messages.Messages.Count}");
    }
}

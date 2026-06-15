using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Example.PluginAuthoring.Examples;

internal static class JsonUploadExample
{
    public static async Task RunAsync()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = Plugins.PluginServer.Create(messages, defaultPolicy: PluginExamplePolicies.MessageWrite());
        server.RegisterEventAdapter(DamageEventAdapter.Instance);

        var uploadJson = PluginPackageJsonSerializer.Export(FireDamagePluginPackage.Create());
        await server.InstallJsonAsync(uploadJson);
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-json"));

        Console.WriteLine($"json upload: messages={messages.Messages.Count}");
    }
}

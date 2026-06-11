using SafeIR.PluginSamples.Common;
using SafeIR.Plugins;

var messages = new InMemoryPluginMessageSink();
var server = PluginServer.Create(messages);

var serverGate = server.BindValue("serverGateMinDamage", 0);
var groupedSettings = server.BindContext<IFireDamageSettings>(
    "operatorDefaults",
    settings => {
        settings.Enabled = true;
        settings.DamageType = "fire";
        settings.MinDamage = 100;
    });

var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
server.Hooks.On(DamageEventAdapter.Instance)
    .Where((e, _) => e.Amount >= serverGate.Value)
    .UseKernel(kernel);

await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
kernel.Value.Set("MinDamage", 250);
await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

var typedKernel = server.Kernels.Get<IFireDamageSettings>("fire-damage");
typedKernel.Value.DamageType = "ice";
await server.Hooks.PublishAsync(new DamageEvent("ice", 300, "player-2"));

Console.WriteLine("Live context defaults:");
Console.WriteLine($"  {groupedSettings.Value.DamageType} >= {groupedSettings.Value.MinDamage}");
Console.WriteLine("Messages:");
foreach (var message in messages.Messages) {
    Console.WriteLine($"  {message.TargetId}: {message.Message}");
}

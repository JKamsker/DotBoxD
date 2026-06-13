using SafeIR.Game.PluginHost;
using SafeIR.Transport.Ipc;

if (args.Length != 1)
{
    await Console.Error.WriteLineAsync("Usage: SafeIR.Game.PluginHost <named-pipe-name>").ConfigureAwait(false);
    return 1;
}

var pipeName = args[0];

// (1) Preview the kernels locally before shipping anything.
await LocalPreview.RunAsync().ConfigureAwait(false);

// (2) Connect to the game server's control plane.
Console.WriteLine($"[host] connecting to server pipe '{pipeName}'...");
await using var connection = await SafeIrShaRpcMessagePackIpc.ConnectNamedPipeAsync(pipeName).ConfigureAwait(false);
var service = connection.Get<IGamePluginControlService>();

// (3) Ship each kernel as opaque verified IR (the server never sees kernel source).
var guardianJson = PluginPackageJsonSerializer.Export(GuardianPluginPackage.Create());
var guardianId = await service.InstallPluginAsync(guardianJson).ConfigureAwait(false);
Console.WriteLine($"[host] shipped kernel as opaque IR -> installed plugin '{guardianId}'.");

var retaliationJson = PluginPackageJsonSerializer.Export(RetaliationPluginPackage.Create());
var retaliationId = await service.InstallPluginAsync(retaliationJson).ConfigureAwait(false);
Console.WriteLine($"[host] shipped kernel as opaque IR -> installed plugin '{retaliationId}'.");

// (4) Tune live settings over IPC (atomic batch).
await service.UpdateSettingsAsync(
        "guardian",
        [
            new LiveSettingUpdate("CalmStrength", "35"),
            new LiveSettingUpdate("AggroRange", "6")
        ],
        atomic: true)
    .ConfigureAwait(false);
Console.WriteLine("[host] tuned guardian live settings over IPC (CalmStrength=35, AggroRange=6).");

// (5) Summary, then exit so the server proceeds to the with-plugin phase.
Console.WriteLine("[host] done: 2 kernels shipped, settings updated. Exiting.");
return 0;

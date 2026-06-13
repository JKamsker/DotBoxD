namespace SafeIR.Game.Plugin;

using SafeIR.Transport.Ipc;

/// <summary>
/// The plugin process. Connects to the game server's control plane, ships each kernel as verified IR
/// (the server never sees kernel source), tunes live settings over IPC, then exits so the server
/// proceeds to its with-plugin phase.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            await Console.Error.WriteLineAsync("Usage: SafeIR.Game.Plugin <named-pipe-name>").ConfigureAwait(false);
            return 1;
        }

        var pipeName = args[0];

        // Connect to the game server's control plane.
        Console.WriteLine($"[plugin] connecting to server pipe '{pipeName}'...");
        await using var connection = await SafeIrShaRpcMessagePackIpc.ConnectNamedPipeAsync(pipeName).ConfigureAwait(false);
        var service = connection.Get<IGamePluginControlService>();

        // Ship each kernel as verified IR.
        var guardianId = await service
            .InstallPluginAsync(PluginPackageJsonSerializer.Export(GuardianPluginPackage.Create()))
            .ConfigureAwait(false);
        Console.WriteLine($"[plugin] installed kernel '{guardianId}'.");

        var retaliationId = await service
            .InstallPluginAsync(PluginPackageJsonSerializer.Export(RetaliationPluginPackage.Create()))
            .ConfigureAwait(false);
        Console.WriteLine($"[plugin] installed kernel '{retaliationId}'.");

        // Tune live settings over IPC (atomic batch).
        await service.UpdateSettingsAsync(
                "guardian",
                [
                    new LiveSettingUpdate("CalmStrength", "35"),
                    new LiveSettingUpdate("AggroRange", "6")
                ],
                atomic: true)
            .ConfigureAwait(false);
        Console.WriteLine("[plugin] tuned guardian live settings (CalmStrength=35, AggroRange=6).");

        Console.WriteLine("[plugin] done: 2 kernels shipped, settings updated. Exiting.");
        return 0;
    }
}

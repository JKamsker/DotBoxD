using DotBoxD.Kernels.Game.Plugin.Kernels;
using DotBoxD.Kernels.Game.Server.Abstractions;

namespace DotBoxD.Kernels.Game.Plugin;

/// <summary>
/// The plugin process from the developer's seat. Everything here is hand-written; the GamePluginServer
/// facade, the IGameWorldAccess RPC proxy, the install verbs, and the InvokeAsync plumbing are all
/// generated from the interfaces in Examples.GameServer.Server.Abstractions.
///
/// The shape: the server implements IGameWorldAccess; the plugin holds a generated RPC proxy of the same
/// interface (that proxy IS `server`); kernels get it injected. One surface, three call sites.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            await Console.Error.WriteLineAsync("Usage: Examples.GameServer.Plugin <named-pipe-name>")
                .ConfigureAwait(false);
            return 1;
        }

        var pipeName = args[0];
        Console.WriteLine($"[plugin] connecting to server pipe '{pipeName}'...");

        // Build() is synchronous and does no I/O; StartAsync connects.
        using var server = GamePluginServerBuilder.FromPipeName(pipeName).Build();
        await server.StartAsync().ConfigureAwait(false);

        // Install plugin-owned kernels — ships verified IR. `server` IS the world surface, so the install
        // verbs sit right on it (Replace) and on each control (Monsters.Extend).
        Console.WriteLine("[plugin] installing GuardianKernel...");
        await server.Replace<IMonsterAggroService, GuardianKernel>().ConfigureAwait(false);
        Console.WriteLine("[plugin] installing RetaliationKernel...");
        await server.Replace<IAttackService, RetaliationKernel>().ConfigureAwait(false);
        Console.WriteLine("[plugin] installing MonsterKillerKernel...");
        await server.Monsters.Extend<IMonsterKillerService, MonsterKillerKernel>().ConfigureAwait(false);

        // Tune a replaced service's live settings — strongly typed, one atomic IPC batch.
        await server.Get<GuardianKernel>()
            .SetValuesAsync(k => { k.CalmStrength = "35"; k.AggroRange = 6; }, atomic: true)
            .ConfigureAwait(false);

        // Direct domain call — the SAME IGameWorldAccess.Monsters.KillAsync the server implements and the
        // kernels call. No [WireCall], no separate wire name.
        var killed = await server.Monsters.KillAsync("monster-4").ConfigureAwait(false);
        Console.WriteLine($"[plugin] server.Monsters.KillAsync(monster-4) => {killed}.");

        // Generated server-extension graft (from MonsterKillerKernel onto IMonsterControl).
        var killResults = await server.Monsters
            .KillMonstersAsync(["monster-3", "monster-4", "player-1"])
            .ConfigureAwait(false);
        Console.WriteLine($"[plugin] server.Monsters.KillMonstersAsync(...) => {killResults.Count} results.");

        var health = await server.Entities.GetHealthAsync("monster-2").ConfigureAwait(false);
        Console.WriteLine($"[plugin] server.Entities.GetHealthAsync(monster-2) => {health}.");

        // Anonymous server-side invoke — the lambda is lowered to verified IR and runs sandboxed. It reads
        // the same async world surface (local on the server).
        var monsterHealth = await server.InvokeAsync(async (IGameWorldAccess world) =>
        {
            var monster = await world.Monsters.GetAsync("monster-2");
            return monster.Health;
        }).ConfigureAwait(false);
        Console.WriteLine($"[plugin] InvokeAsync Monsters.GetAsync(monster-2).Health => {monsterHealth}.");

        var implicitMonsterId = "monster-2";
        var implicitLastHealth = 0;
        var implicitMonsterName = await server.InvokeAsync(async (IGameWorldAccess world) =>
        {
            var monster = await world.Monsters.GetAsync(implicitMonsterId);
            implicitLastHealth = monster.Health;
            return monster.Name;
        }).ConfigureAwait(false);
        Console.WriteLine(
            $"[plugin] InvokeAsync implicit capture {implicitMonsterId} => {implicitMonsterName} hp={implicitLastHealth}.");

        // Explicit capture-bag invoke — sync values in and out across the sandbox boundary.
        var capture = new MonsterProbeCapture { MonsterId = "monster-2" };
        var monsterName = await server.InvokeAsync(
            capture,
            async (IGameWorldAccess world, MonsterProbeCapture bag) =>
            {
                var monster = await world.Monsters.GetAsync(bag.MonsterId);
                bag.LastHealth = monster.Health;
                return monster.Name;
            }).ConfigureAwait(false);
        Console.WriteLine($"[plugin] InvokeAsync capture {capture.MonsterId} => {monsterName} hp={capture.LastHealth}.");

        Console.WriteLine("[plugin] kernels live; holding until server completes...");
        await server.HoldUntilShutdownAsync().ConfigureAwait(false);
        return 0;
    }
}

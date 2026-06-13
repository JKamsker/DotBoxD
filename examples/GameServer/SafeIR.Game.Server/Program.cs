namespace SafeIR.Game.Server;

using System.Globalization;
using SafeIR.Transport.Ipc;

/// <summary>
/// The game server (parent process): a deterministic 1D simulation that runs a baseline phase with no
/// plugins, launches the plugin child process to ship verified kernels over IPC, then runs a
/// with-plugin phase showing the sandboxed kernels change behavior.
/// </summary>
internal static class Program
{
    private const int BaselineTicks = 3;
    private const int PluginTicks = 4;

    public static async Task<int> Main(string[] args)
    {
        // (a) Build the world and plugin server. The command sink is the example-defined capability
        // that turns plugin messages into game-state changes; it is bound to the world once it exists.
        var sink = new GameCommandSink();
        using var server = PluginServer.Create(sink, defaultPolicy: ServerPolicy.Create());
        server.RegisterEventAdapter(MonsterAggroEventAdapter.Instance);
        server.RegisterEventAdapter(AttackEventAdapter.Instance);

        var world = GameWorld.CreateDefault(server.Hooks);
        sink.Bind(world);

        var playerHpBaseline = PlayerHpById(world);

        Console.WriteLine("=== SafeIR Game Server (golden example) ===");
        Console.WriteLine("Low-level players (player-1 lvl1, player-2 lvl3) vs high-level monsters (lvl8).");
        Console.WriteLine();

        // (b) Baseline phase: no plugins. Monsters bully the low-level players.
        Console.WriteLine("--- BASELINE (no plugins) ---");
        PrintWorld(world);
        for (var i = 0; i < BaselineTicks; i++)
        {
            await world.TickAsync().ConfigureAwait(false);
            Console.WriteLine($"[tick {world.Tick}]");
            PrintWorld(world);
        }

        var baselineDamage = TotalDamageTaken(playerHpBaseline, world);
        Console.WriteLine($"Baseline: low-level players took {baselineDamage} total damage in {BaselineTicks} ticks.");
        Console.WriteLine();

        // (c) Start the IPC control plane on a high-entropy pipe name.
        var pipeName = "safe-ir-game-" + Guid.NewGuid().ToString("N");
        var service = new GamePluginControlService(server, sink, world);
        await using var host = SafeIrShaRpcMessagePackIpc.ListenNamedPipe(
            pipeName,
            peer => peer.ProvideGamePluginControlService(service));
        await host.StartAsync().ConfigureAwait(false);
        Console.WriteLine($"[server] listening for plugin on pipe '{pipeName}'.");

        // (d) Launch the plugin child process; (e) wait for it to ship plugins and exit.
        Console.WriteLine("[server] launching plugin child process...");
        var pluginProcess = PluginLauncher.Launch(pipeName);
        await pluginProcess.WaitForExitAsync().ConfigureAwait(false);
        if (pluginProcess.ExitCode != 0)
        {
            await Console.Error.WriteLineAsync($"[server] plugin exited with code {pluginProcess.ExitCode}.").ConfigureAwait(false);
            await host.StopAsync().ConfigureAwait(false);
            return 1;
        }

        Console.WriteLine("[server] plugin finished shipping kernels. Running with-plugin phase.");
        Console.WriteLine();

        // (f) With-plugin phase: the untrusted kernels now run sandboxed and change behavior.
        Console.WriteLine("--- WITH PLUGINS (guardian calms, retaliation taunts) ---");
        var pluginPhaseStart = PlayerHpById(world);
        for (var i = 0; i < PluginTicks; i++)
        {
            await world.TickAsync().ConfigureAwait(false);
            Console.WriteLine($"[tick {world.Tick}]");
            PrintEffects(sink.DrainEffects());
            PrintWorld(world);
        }

        var pluginDamage = TotalDamageTaken(pluginPhaseStart, world);
        var perTickBaseline = (double)baselineDamage / BaselineTicks;
        var perTickPlugin = (double)pluginDamage / PluginTicks;

        // (g) Summary.
        Console.WriteLine();
        Console.WriteLine("=== SUMMARY ===");
        Console.WriteLine(Format("Baseline damage/tick (no plugin)", perTickBaseline));
        Console.WriteLine(Format("With-plugin damage/tick", perTickPlugin));
        Console.WriteLine(perTickPlugin < perTickBaseline
            ? "Plugins reduced bullying: low-level players survive longer than baseline."
            : "Plugins applied (see per-tick effects above).");
        PrintSurvivors(world);

        await host.StopAsync().ConfigureAwait(false);
        return 0;
    }

    private static Dictionary<string, int> PlayerHpById(GameWorld world)
        => world.Players().ToDictionary(p => p.Id, p => p.Hp, StringComparer.Ordinal);

    private static int TotalDamageTaken(IReadOnlyDictionary<string, int> before, GameWorld world)
    {
        var total = 0;
        foreach (var player in world.Players())
        {
            if (before.TryGetValue(player.Id, out var previous))
            {
                total += Math.Max(0, previous - player.Hp);
            }
        }

        return total;
    }

    private static void PrintWorld(GameWorld world)
        => Console.WriteLine(world.Render());

    private static void PrintEffects(IReadOnlyList<string> effects)
    {
        if (effects.Count == 0)
        {
            Console.WriteLine("    (no plugin effects applied this tick)");
            return;
        }

        foreach (var effect in effects)
        {
            Console.WriteLine($"    effect: {effect}");
        }
    }

    private static void PrintSurvivors(GameWorld world)
    {
        foreach (var player in world.Players())
        {
            var state = player.IsAlive ? $"alive (hp {player.Hp})" : "defeated";
            Console.WriteLine($"    {player.Id}: {state}");
        }
    }

    private static string Format(string label, double value)
        => $"{label}: {value.ToString("0.0", CultureInfo.InvariantCulture)}";
}

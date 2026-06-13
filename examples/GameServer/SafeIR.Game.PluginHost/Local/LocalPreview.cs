namespace SafeIR.Game.PluginHost;

/// <summary>
/// Runs the kernels in-process (filter + projection + invoke) before they are shipped, so the
/// author can see exactly which events match and which commands the kernels would emit. This is the
/// same code path the server uses, exercised locally against sample events.
/// </summary>
internal static class LocalPreview
{
    public static async Task RunAsync()
    {
        Console.WriteLine("[host] local preview (in-process filter + projection + invoke):");

        var sink = new RecordingMessageSink();
        using var server = PluginServer.Create(sink, defaultPolicy: PluginHostPolicy.Create());
        server.RegisterEventAdapter(MonsterAggroEventAdapter.Instance);
        server.RegisterEventAdapter(AttackEventAdapter.Instance);

        await server.InstallAsync(GuardianPluginPackage.Create()).ConfigureAwait(false);
        await server.InstallAsync(RetaliationPluginPackage.Create()).ConfigureAwait(false);
        server.Hooks.On<MonsterAggroEvent>().UseKernel<GuardianKernel>();
        server.Hooks.On<AttackEvent>().UseKernel<RetaliationKernel>();

        await PublishAggroAsync(server, sink, new MonsterAggroEvent("monster-1", "player-1", 3, 8, 2));
        await PublishAggroAsync(server, sink, new MonsterAggroEvent("monster-1", "player-2", 9, 8, 2));
        await PublishAttackAsync(server, sink, new AttackEvent("monster-2", "player-1", 7, 8));
        await PublishAttackAsync(server, sink, new AttackEvent("monster-2", "player-3", 1, 1));

        Console.WriteLine();
    }

    private static async Task PublishAggroAsync(
        PluginServer server,
        RecordingMessageSink sink,
        MonsterAggroEvent e)
    {
        var before = sink.Messages.Count;
        await server.Hooks.PublishAsync(e).ConfigureAwait(false);
        var decision = Describe(sink, before);
        Console.WriteLine(
            $"  aggro monster={e.MonsterId} player={e.PlayerId} dist={e.Distance} " +
            $"mLvl={e.MonsterLevel} pLvl={e.PlayerLevel} -> {decision}");
    }

    private static async Task PublishAttackAsync(
        PluginServer server,
        RecordingMessageSink sink,
        AttackEvent e)
    {
        var before = sink.Messages.Count;
        await server.Hooks.PublishAsync(e).ConfigureAwait(false);
        var decision = Describe(sink, before);
        Console.WriteLine(
            $"  attack attacker={e.AttackerId} target={e.TargetId} dmg={e.Damage} " +
            $"aLvl={e.AttackerLevel} -> {decision}");
    }

    private static string Describe(RecordingMessageSink sink, int before)
    {
        if (sink.Messages.Count == before)
        {
            return "filtered (no command)";
        }

        var emitted = sink.Messages[before];
        return $"command \"{emitted.Message}\" to {emitted.TargetId}";
    }
}

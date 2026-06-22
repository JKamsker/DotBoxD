using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Queryable;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Server.Queries;

/// <summary>
/// Demonstrates the opt-in dynamic event-query path (<c>DotBoxD.Queryable</c>) next to the analyzer-lowered
/// <c>Subscriptions.On&lt;T&gt;().Where(...).Select(...).Run(...)</c> path. The query below is composed from
/// values only known at run time (a watched attacker and a damage threshold), captured into a portable
/// filter/projection AST, planned into index constraints, and dispatched through the host's index — so the
/// subscriber sees only matching, projected events rather than every <see cref="AttackEvent"/>.
/// </summary>
internal static class DynamicQueries
{
    public static async ValueTask ConfigureAsync(PluginServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        // In a real host these would come from config, an admin tool, or a remote dashboard. Attackers in
        // this 1D simulation are monsters, so the watched attacker is a monster id.
        var watchedAttacker = "monster-1";
        var minimumDamage = 5;

        var handle = await server.Subscriptions.Query<AttackEvent>()
            .Where(e => e.AttackerId == watchedAttacker && e.Damage >= minimumDamage)
            .Select(e => new AttackNotice(e.AttackerId, e.TargetId, e.Damage))
            .SubscribeAsync((notice, ctx) =>
            {
                Console.WriteLine(
                    $"[server] query-watch hit: {notice.AttackerId} -> {notice.TargetId} (damage {notice.Damage})");

                // The host defines what messages mean; an unknown verb is ignored safely by the command sink.
                ctx.Messages.Send(notice.TargetId, $"query-watch:{notice.AttackerId}:{notice.Damage}");
                return ValueTask.CompletedTask;
            })
            .ConfigureAwait(false);

        // The host can log exactly how it planned the dynamic subscription (index constraints, projection,
        // coverage) from the preserved query AST — the same metadata model the generated path produces.
        foreach (var line in handle.Describe())
        {
            Console.WriteLine($"[server] {line}");
        }
    }
}

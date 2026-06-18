namespace DotBoxD.Kernels.Game.Plugin.Kernels;

/// <summary>A 1D world position — the smallest grouped value object, nested inside <see cref="WorldRangeQuery"/>.</summary>
public readonly record struct WorldPoint(int Position);

/// <summary>
/// A grouped spatial query passed as ONE value-object parameter (issue #41): where to search, the inclusive
/// radius around it, and how many monsters to act on at most. Passing a single record instead of three loose
/// <c>int</c>s removes argument-order bugs and reads naturally at the call site —
/// <c>Monsters.KillMonstersInRangeAsync(new WorldRangeQuery(new WorldPoint(2), 3, 2), ids)</c>.
/// </summary>
public readonly record struct WorldRangeQuery(WorldPoint Center, int Radius, int MaxResults);

/// <summary>
/// Spatial variant of <see cref="MonsterKillerKernel"/> that takes a <see cref="WorldRangeQuery"/> value object
/// instead of three loose primitives, demonstrating record/DTO parameters (including a nested
/// <see cref="WorldPoint"/>) on a <c>[ServerExtensionMethod]</c> entrypoint. The generated client marshals the
/// record to the wire and the server reads its nested + scalar fields through the same DTO marshalling used for
/// results. A monster is killed only when its 1D position is within <c>Center.Position ± Radius</c>, while the
/// running result count stays under <c>MaxResults</c>.
/// </summary>
[ServerExtension(typeof(IMonsterControl))]
public sealed partial class RangeMonsterKillerKernel
{
    private readonly IGameWorldAccess _world;

    public RangeMonsterKillerKernel(IGameWorldAccess world) => _world = world;

    [ServerExtensionMethod]   // grafted as IMonsterControl.KillMonstersInRangeAsync (name = the method's name)
    public async ValueTask<List<MonsterKillResult>> KillMonstersInRangeAsync(
        WorldRangeQuery query,
        List<string> monsterIds,
        HookContext ctx)
    {
        var results = new List<MonsterKillResult>();
        foreach (var id in monsterIds)
        {
            var monster = _world.Monsters.Get(id);            // scoped handle — id captured once
            var healthBefore = await monster.GetHealthAsync();
            var wasMonster = await _world.Monsters.IsMonsterAsync(id);
            var level = await monster.GetLevelAsync();
            var position = await monster.GetPositionAsync();
            var inRange =
                position >= query.Center.Position - query.Radius &&
                position <= query.Center.Position + query.Radius;
            var killed =
                wasMonster && healthBefore > 0 && inRange &&
                results.Count < query.MaxResults &&
                await monster.KillAsync();
            results.Add(new MonsterKillResult(id, wasMonster, level, position, healthBefore, killed));
        }

        return results;
    }
}

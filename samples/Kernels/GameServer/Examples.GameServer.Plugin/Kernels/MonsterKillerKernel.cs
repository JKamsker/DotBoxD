namespace DotBoxD.Kernels.Game.Plugin;

public interface IMonsterKillerService
{
    ValueTask<List<MonsterKillResult>> KillMonstersAsync(List<string> monsterIds);
}

public readonly record struct MonsterKillResult(
    string MonsterId,
    bool WasMonster,
    int Level,
    int Position,
    int HealthBefore,
    bool Killed);

/// <summary>
/// Plugin-owned batch operation. It is injected the SAME <see cref="IGameWorldAccess"/> the plugin uses
/// remotely — but because this kernel runs on the server, the awaited calls are local (no real IPC hop).
/// From the dev's seat it reads exactly like the remote plugin code: <c>await _world.Monsters.KillAsync(id)</c>.
/// The generated verified IR is installed and executed server-side through the server-extension bridge.
/// </summary>
[ServerExtensionClient(typeof(IMonsterControl))]
[ServerExtension("monster-killer", typeof(IMonsterKillerService))]
public sealed partial class MonsterKillerKernel
{
    private readonly IGameWorldAccess _world;

    public MonsterKillerKernel(IGameWorldAccess world) => _world = world;

    [ServerExtensionMethod(typeof(IMonsterControl), "KillMonstersAsync")]
    public async ValueTask<List<MonsterKillResult>> KillMonstersAsync(List<string> monsterIds, HookContext ctx)
    {
        var results = new List<MonsterKillResult>();
        foreach (var id in monsterIds)
        {
            var healthBefore = await _world.Entities.GetHealthAsync(id);
            var wasMonster = await _world.Monsters.IsMonsterAsync(id);
            var level = await _world.Entities.GetLevelAsync(id);
            var position = await _world.Entities.GetPositionAsync(id);
            var killed = false;
            if (wasMonster && healthBefore > 0)
            {
                killed = await _world.Monsters.KillAsync(id);
            }

            results.Add(new MonsterKillResult(id, wasMonster, level, position, healthBefore, killed));
        }

        return results;
    }
}

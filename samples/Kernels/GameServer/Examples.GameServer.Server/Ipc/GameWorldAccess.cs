using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Game.Server.Simulation;

namespace DotBoxD.Kernels.Game.Server.Ipc;

/// <summary>
/// The server's real implementation of <see cref="IGameWorldAccess"/> over the live <see cref="GameWorld"/>.
/// The plugin gets an RPC proxy of this same interface (its <c>GamePluginServer</c>); kernels get it
/// injected. Calls are synchronous against the in-process world, returned as completed
/// <see cref="ValueTask"/>s — the async shape only exists so the remote proxy and in-sandbox kernels share
/// one contract. Each domain method carries its <see cref="HostCapabilityAttribute"/>; this is the single
/// server-side source of capability metadata (the abstraction stays pure).
/// </summary>
internal sealed class GameWorldAccess : ServerControlBase, IGameWorldAccess
{
    private readonly Func<GameWorld> _world;

    public GameWorldAccess(GameWorld world) : this(() => world)
    {
    }

    public GameWorldAccess(Func<GameWorld> world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        Monsters = new GameMonsterControl(world);
        Entities = new GameEntityControl(world);
    }

    public IMonsterControl Monsters { get; }

    public IEntityControl Entities { get; }

    // Root service verbs are control-plane (plugin-side); never invoked on the server. See ServerControlBase.
    public ValueTask<string> Replace<TService, TKernel>()
        where TService : class
        where TKernel : class, TService
        => throw ControlPlaneOnly();

    public ILiveSettingsHandle<TKernel> Get<TKernel>()
        where TKernel : class, new()
        => throw ControlPlaneOnly();
}

internal sealed class GameMonsterControl : ServerControlBase, IMonsterControl
{
    private readonly Func<GameWorld> _world;

    public GameMonsterControl(Func<GameWorld> world)
        => _world = world ?? throw new ArgumentNullException(nameof(world));

    [HostCapability("game.world.monster.read.snapshot")]
    public ValueTask<MonsterSnapshot> GetAsync(string entityId)
        => ValueTask.FromResult(_world().GetMonsterSnapshot(entityId));

    [HostCapability("game.world.monster.write.kill")]   // effect (HostStateWrite) inferred from the impl
    public ValueTask<bool> KillAsync(string entityId)
        => ValueTask.FromResult(_world().KillMonster(entityId));

    [HostCapability("game.world.monster.read.kind")]
    public ValueTask<bool> IsMonsterAsync(string entityId)
        => ValueTask.FromResult(_world().IsMonster(entityId));

    // Deliberately a different capability subtree (combat.*, not monster.*) — the kind of exception that
    // a naming convention could not infer, which is exactly why the capability is stated explicitly here.
    [HostCapability("game.world.combat.threat")]
    public ValueTask<int> GetThreatAsync(string entityId)
        => ValueTask.FromResult(_world().GetLevel(entityId));
}

internal sealed class GameEntityControl : ServerControlBase, IEntityControl
{
    private readonly Func<GameWorld> _world;

    public GameEntityControl(Func<GameWorld> world)
        => _world = world ?? throw new ArgumentNullException(nameof(world));

    [HostCapability("game.world.monster.read.health")]
    public ValueTask<int> GetHealthAsync(string entityId)
        => ValueTask.FromResult(_world().GetHealth(entityId));

    [HostCapability("game.world.monster.read.level")]
    public ValueTask<int> GetLevelAsync(string entityId)
        => ValueTask.FromResult(_world().GetLevel(entityId));

    [HostCapability("game.world.monster.read.position")]
    public ValueTask<int> GetPositionAsync(string entityId)
        => ValueTask.FromResult(_world().GetPosition(entityId));
}

/// <summary>
/// OPEN QUESTION made concrete: <see cref="IExtensibleControl"/>'s install verb sits on every control, but
/// the SERVER can't meaningfully implement it — installs arrive as verified IR through the control-plane,
/// not as a generic call with a plugin kernel type. So every server-side control throws here. That this
/// base exists is the signal that <c>Extend</c>/<c>Replace</c>/<c>Get</c> probably belong on the PLUGIN
/// facade only, leaving <see cref="IGameWorldAccess"/> a pure domain contract.
/// </summary>
internal abstract class ServerControlBase : IExtensibleControl
{
    public ValueTask<string> Extend<TService, TKernel>()
        where TService : class
        where TKernel : class
        => throw ControlPlaneOnly();

    protected static NotSupportedException ControlPlaneOnly()
        => new("Install verbs (Extend/Replace/Get) run plugin-side; the server fulfils installs via the control-plane.");
}

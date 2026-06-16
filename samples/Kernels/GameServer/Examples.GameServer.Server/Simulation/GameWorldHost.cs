using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Game.Server.Abstractions;
using DotBoxD.Kernels.Game.Server.Ipc;

namespace DotBoxD.Kernels.Game.Server.Simulation;

/// <summary>
/// Server-side wiring for the gated kernel surface (the bindings a kernel reaches through
/// <c>ctx.Host&lt;IGameWorldAccess&gt;()</c>).
///
/// UNIFIED-DESIGN NOTE: the sandbox host bindings are now DERIVED from the server's
/// <see cref="GameWorldAccess"/> impl and its <c>[HostCapability]</c> annotations — one server-side source
/// of truth. For each interface method the framework registers one capability-gated binding: the binding
/// id (routing) comes from the method identity, the capability from the annotation, the read/write effect
/// from the implementation. No hand-typed binding ids or capability strings live here anymore — compare
/// the old <c>AddBinding(SnapshotBinding("host.world.getMonster", "game.world.monster.read.snapshot", …))</c>
/// registry this replaces, which duplicated what the abstraction's <c>[HostBinding]</c> already said.
/// </summary>
internal sealed class GameWorldHost
{
    private GameWorld? _world;

    /// <summary>Bound after the world is built (the world needs the hooks, the bindings need the world).</summary>
    public void Bind(GameWorld world) => _world = world;

    public void AddBindings(SandboxHostBuilder builder)
        // VIBE: the framework reflects [HostCapability] off the IGameWorldAccess impl and registers one
        // gated binding per method. Routing + effects are derived; only the capability is read from the
        // annotation. (Speculative API name — the point is "derive from the impl", not hand-write a registry.)
        => builder.AddBindingsFrom<IGameWorldAccess>(new GameWorldAccess(_world!));
}

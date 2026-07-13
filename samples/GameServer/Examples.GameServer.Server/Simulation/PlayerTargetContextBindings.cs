using DotBoxD.Kernels.Game.Server.Abstractions.Events;

namespace DotBoxD.Kernels.Game.Server.Simulation;

/// <summary>
/// Hand-written host-side equivalent of the value-object sugar. The public <c>AddBindingsFrom</c> primitive
/// registers the route derived by <see cref="HostBindingObjectAttribute"/> and receives the SDK value object
/// as argument zero.
/// </summary>
internal sealed class PlayerTargetContextBindings(Func<GameWorld> world) : IPlayerTargetContextBindings
{
    public bool IsBelowLevel(PlayerTargetContext player, int maximumLevel)
        => world().GetLevel(player.PlayerId) <= maximumLevel;
}

internal interface IPlayerTargetContextBindings
{
    [HostBinding(
        "host.game.player.IsBelowLevel.i32",
        "game.world.entity.read.level",
        SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    bool IsBelowLevel(PlayerTargetContext player, int maximumLevel);
}

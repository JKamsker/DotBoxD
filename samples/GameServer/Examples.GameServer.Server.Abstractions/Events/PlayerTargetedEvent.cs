using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Game.Server.Abstractions.Events;

/// <summary>
/// The player value object carried by <see cref="PlayerTargetedEvent"/>. Every eligible public instance
/// method is a receiver-forwarding host binding with these defaults; individual methods need attributes only
/// to override metadata or opt out.
/// </summary>
[HostBindingObject(
    "host.game.player",
    "game.world.entity.read.level",
    SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
public sealed record PlayerTargetContext(string PlayerId)
{
    /// <summary>Reads the player's current server-owned level while the lowered chain runs.</summary>
    public bool IsBelowLevel(int maximumLevel)
        => throw new NotSupportedException("Host binding methods run only after DotBoxD lowers them to verified IR.");

    /// <summary>A normal local helper that is deliberately absent from the sandbox binding surface.</summary>
    [HostBindingIgnore]
    public string LocalLabel() => "player:" + PlayerId;
}

/// <summary>Published when a monster selects a player, before it moves or attacks.</summary>
public sealed record PlayerTargetedEvent(string MonsterId, string PlayerId, PlayerTargetContext Player);

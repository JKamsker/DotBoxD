namespace SafeIR.Game.Server.Abstractions;

using SafeIR;
using SafeIR.Plugins;

/// <summary>
/// Published when a monster attacks an adjacent player. Plugins subscribe to taunt strong attackers
/// away from the target they are bullying.
/// </summary>
public sealed record AttackEvent(
    string AttackerId,
    string TargetId,
    int Damage,
    int AttackerLevel);

public sealed class AttackEventAdapter : IPluginEventAdapter<AttackEvent>
{
    public static AttackEventAdapter Instance { get; } = new();

    public string EventName => "AttackEvent";

    public IReadOnlyList<Parameter> Parameters { get; } = [
        new("e_AttackerId", SandboxType.String),
        new("e_TargetId", SandboxType.String),
        new("e_Damage", SandboxType.I32),
        new("e_AttackerLevel", SandboxType.I32)
    ];

    public IReadOnlyList<SandboxValue> ToSandboxValues(AttackEvent e)
        => [
            SandboxValue.FromString(e.AttackerId),
            SandboxValue.FromString(e.TargetId),
            SandboxValue.FromInt32(e.Damage),
            SandboxValue.FromInt32(e.AttackerLevel)
        ];
}

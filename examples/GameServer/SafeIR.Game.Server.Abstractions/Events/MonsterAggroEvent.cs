namespace SafeIR.Game.Server.Abstractions;

using SafeIR;
using SafeIR.Plugins;

/// <summary>
/// Published when a monster detects a player within sensing range. Plugins subscribe to decide
/// whether to calm the monster so weak players are not bullied.
/// </summary>
public sealed record MonsterAggroEvent(
    string MonsterId,
    string PlayerId,
    int Distance,
    int MonsterLevel,
    int PlayerLevel);

public sealed class MonsterAggroEventAdapter : IPluginEventAdapter<MonsterAggroEvent>
{
    public static MonsterAggroEventAdapter Instance { get; } = new();

    public string EventName => "MonsterAggroEvent";

    public IReadOnlyList<Parameter> Parameters { get; } = [
        new("e_MonsterId", SandboxType.String),
        new("e_PlayerId", SandboxType.String),
        new("e_Distance", SandboxType.I32),
        new("e_MonsterLevel", SandboxType.I32),
        new("e_PlayerLevel", SandboxType.I32)
    ];

    public IReadOnlyList<SandboxValue> ToSandboxValues(MonsterAggroEvent e)
        => [
            SandboxValue.FromString(e.MonsterId),
            SandboxValue.FromString(e.PlayerId),
            SandboxValue.FromInt32(e.Distance),
            SandboxValue.FromInt32(e.MonsterLevel),
            SandboxValue.FromInt32(e.PlayerLevel)
        ];
}

namespace DotBoxD.Kernels.Game.Server.Abstractions.Events;

public abstract record DamageDecisionEventBase(string MonsterId);

[Hook("game.damage.decision", typeof(RemoteDamageDecisionResult))]
public sealed record RemoteDamageDecisionEvent(string MonsterId, int Damage)
    : DamageDecisionEventBase(MonsterId);

[HookResult]
public readonly partial record struct RemoteDamageDecisionResult(
    bool Success,
    string? Reason,
    int Damage) : IHookResult;

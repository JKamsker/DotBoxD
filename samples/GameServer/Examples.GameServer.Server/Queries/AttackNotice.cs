namespace DotBoxD.Kernels.Game.Server.Queries;

/// <summary>The projected payload a dynamic <see cref="DynamicQueries"/> subscription dispatches per matched attack.</summary>
public sealed record AttackNotice(string AttackerId, string TargetId, int Damage);

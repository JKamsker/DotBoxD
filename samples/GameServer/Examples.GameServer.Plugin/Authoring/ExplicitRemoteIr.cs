using DotBoxD.Kernels.Game.Server.Abstractions.Events;

namespace DotBoxD.Kernels.Game.Plugin.Authoring;

internal static class ExplicitRemoteIr
{
    public static IRFunc<MonsterAggroEvent, bool> MonsterAggroDistanceAtMost(int max)
        => IRBuilder.For<MonsterAggroEvent>().Filter(ir => ir.LessThanOrEqual(ir.Field(2), ir.Int32(max)));

    public static IRFunc<MonsterAggroEvent, string> MonsterAggroMonsterId()
        => IRBuilder.For<MonsterAggroEvent>().Projection<string>(ir => ir.Field(0));

    public static IRFunc<AttackEvent, bool> AttackDamageAtLeast(int min)
        => IRBuilder.For<AttackEvent>().Filter(ir => ir.GreaterThanOrEqual(ir.Field(2), ir.Int32(min)));

    public static IRFunc<AttackEvent, bool> AttackPlayerDamageAtLeast(string attackerId, int min)
        => IRBuilder.For<AttackEvent>().Filter(ir => ir.And(
            ir.Equal(ir.Field(0), ir.String(attackerId)),
            ir.GreaterThanOrEqual(ir.Field(2), ir.Int32(min))));

    public static IRFunc<AttackEvent, string> AttackAttackerId()
        => IRBuilder.For<AttackEvent>().Projection<string>(ir => ir.Field(0));

    public static IRFunc<AttackEvent, string> AttackTargetId()
        => IRBuilder.For<AttackEvent>().Projection<string>(ir => ir.Field(1));

    public static IRFunc<RemoteDamageDecisionEvent, bool> RemoteDamageGreaterThan(int min)
        => IRBuilder.For<RemoteDamageDecisionEvent>().Filter(ir => ir.GreaterThan(ir.Field(1), ir.Int32(min)));
}

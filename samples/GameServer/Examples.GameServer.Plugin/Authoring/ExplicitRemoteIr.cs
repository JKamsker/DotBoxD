using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Game.Plugin.Authoring;

internal static class ExplicitRemoteIr
{
    private const string CurrentName = "$dotboxd.current";
    private static readonly SourceSpan Span = new(1, 1);

    public static IRFunc<MonsterAggroEvent, bool> MonsterAggroDistanceAtMost(int max)
        => Filter<MonsterAggroEvent>(Le(Field<MonsterAggroEvent>(2), I32(max)));

    public static IRFunc<MonsterAggroEvent, string> MonsterAggroMonsterId()
        => Projection<MonsterAggroEvent, string>(Field<MonsterAggroEvent>(0));

    public static IRFunc<AttackEvent, bool> AttackDamageAtLeast(int min)
        => Filter<AttackEvent>(Ge(Field<AttackEvent>(2), I32(min)));

    public static IRFunc<AttackEvent, bool> AttackPlayerDamageAtLeast(string attackerId, int min)
        => Filter<AttackEvent>(And(Eq(Field<AttackEvent>(0), Text(attackerId)), Ge(Field<AttackEvent>(2), I32(min))));

    public static IRFunc<AttackEvent, string> AttackAttackerId()
        => Projection<AttackEvent, string>(Field<AttackEvent>(0));

    public static IRFunc<AttackEvent, string> AttackTargetId()
        => Projection<AttackEvent, string>(Field<AttackEvent>(1));

    public static IRFunc<RemoteDamageDecisionEvent, bool> RemoteDamageGreaterThan(int min)
        => Filter<RemoteDamageDecisionEvent>(Gt(Field<RemoteDamageDecisionEvent>(1), I32(min)));

    private static IRFunc<TInput, bool> Filter<TInput>(Expression value)
        => IRFunc<TInput, bool>.FromStep(Step<TInput, bool>(LoweredPipelineStepKind.Filter, value));

    private static IRFunc<TInput, TOutput> Projection<TInput, TOutput>(Expression value)
        => IRFunc<TInput, TOutput>.FromStep(Step<TInput, TOutput>(LoweredPipelineStepKind.Projection, value));

    private static LoweredPipelineStep Step<TInput, TOutput>(LoweredPipelineStepKind kind, Expression value)
        => new(
            kind,
            TypeName(typeof(TInput)),
            TypeName(typeof(TOutput)),
            [new Parameter(CurrentName, KernelRpcMarshaller.SandboxTypeOf(typeof(TInput)))],
            [],
            value,
            [],
            []);

    private static CallExpression Field<TInput>(int index)
        => new("record.get", [Current(), I32(index)], null, Span);

    private static VariableExpression Current()
        => new(CurrentName, Span);

    private static LiteralExpression I32(int value)
        => new(SandboxValue.FromInt32(value), Span);

    private static LiteralExpression Text(string value)
        => new(SandboxValue.FromString(value), Span);

    private static BinaryExpression And(Expression left, Expression right)
        => new(left, "&&", right, Span);

    private static BinaryExpression Eq(Expression left, Expression right)
        => new(left, "==", right, Span);

    private static BinaryExpression Ge(Expression left, Expression right)
        => new(left, ">=", right, Span);

    private static BinaryExpression Gt(Expression left, Expression right)
        => new(left, ">", right, Span);

    private static BinaryExpression Le(Expression left, Expression right)
        => new(left, "<=", right, Span);

    private static string TypeName(Type type)
        => type.FullName ?? type.Name;
}

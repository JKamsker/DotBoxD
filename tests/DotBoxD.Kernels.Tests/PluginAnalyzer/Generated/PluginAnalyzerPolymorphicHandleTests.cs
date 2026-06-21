using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerPolymorphicHandleTests
{
    [Fact]
    public void Result_hook_polymorphic_filter_lowers_discriminator_and_scoped_host_call()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant attacker &&
                                      attacker.HasEquippedItem(9001L))
                        .Where(ctx => ctx.Victim is MonsterCombatant)
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage * 2 }, 100);
            }
            """));
        var generated = string.Join(Environment.NewLine, result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK113");
        Assert.Contains("combatant.player.is", generated);
        Assert.Contains("combatant.player.hasEquippedItem", generated);
        Assert.Contains("combatant.monster.is", generated);
        Assert.Contains("combatant.player.read", generated);
        Assert.Contains("combatant.monster.read", generated);
    }

    [Fact]
    public void Result_hook_polymorphic_filter_without_declared_subtype_fails_safe()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Victim is MonsterCombatant)
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """, includeMonsterSubtype: false));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK113");
    }

    [Fact]
    public void Result_hook_declaration_pattern_inside_or_fails_safe()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => (ctx.Attacker is PlayerCombatant attacker &&
                                       attacker.HasEquippedItem(9001L)) ||
                                      ctx.Damage > 0)
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK113");
    }

    private static string Source(string usage, bool includeMonsterSubtype = true)
    {
        var monsterSubtype = includeMonsterSubtype
            ? """[HandleSubtype(typeof(MonsterCombatant), "monster", "combatant.monster", "combatant.monster.read")]"""
            : string.Empty;

        return $$"""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            [PolymorphicHandle(nameof(Id))]
            [HandleSubtype(typeof(PlayerCombatant), "player", "combatant.player", "combatant.player.read")]
            {{monsterSubtype}}
            public abstract record Combatant(long Id);

            public sealed record PlayerCombatant(long Id) : Combatant(Id)
            {
                [HostBinding(
                    "combatant.player.hasEquippedItem",
                    "combatant.player.read",
                    SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                public bool HasEquippedItem(long itemRuntimeId) => throw new System.NotSupportedException();
            }

            public sealed record MonsterCombatant(long Id) : Combatant(Id);

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(Combatant Attacker, Combatant Victim, int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            {{usage}}
            """;
    }
}

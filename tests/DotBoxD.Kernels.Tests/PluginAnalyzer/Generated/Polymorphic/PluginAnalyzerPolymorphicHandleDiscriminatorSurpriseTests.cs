namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerPolymorphicHandleTests
{
    [Fact]
    public void Result_hook_polymorphic_filter_with_malformed_discriminator_fails_safe()
        => AssertFailsSafe(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant attacker &&
                                      attacker.HasEquippedItem(9001L))
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, priority: 0);
            }
            """, playerDiscriminator: "player bad id"));
}

using DotBoxD.Abstractions;

namespace DotBoxD.Plugins.Generated.Tests.HookResults;

internal sealed record CrossResultContextA(HookContext Raw, string Label)
{
    public static CrossResultContextA Create(HookContext raw) => new(raw, "a");
}

internal sealed record CrossResultContextB(HookContext Raw, string Label)
{
    public static CrossResultContextB Create(HookContext raw) => new(raw, "b");
}

public sealed class ResultHookCrossContextDispatchTests
{
    [Fact]
    public async Task Higher_priority_result_wins_across_context_pipelines()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext, CrossResultContextA>(CrossResultContextA.Create)
            .RegisterLocal((ctx, serverContext) => new CombatDamageResult
            {
                Success = true,
                Reason = serverContext.Label,
                Damage = 1,
            }, priority: 0);
        server.Hooks.On<CombatDamageContext, CrossResultContextB>(CrossResultContextB.Create)
            .RegisterLocal((ctx, serverContext) => new CombatDamageResult
            {
                Success = true,
                Reason = serverContext.Label,
                Damage = 999,
            }, priority: 100);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 50));

        Assert.Equal(999, result!.Value.Damage);
        Assert.Equal("b", result.Value.Reason);
    }

    [Fact]
    public async Task Equal_priority_result_tie_uses_global_install_order_across_context_pipelines()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        _ = server.Hooks.On<CombatDamageContext, CrossResultContextA>(CrossResultContextA.Create);

        server.Hooks.On<CombatDamageContext, CrossResultContextB>(CrossResultContextB.Create)
            .RegisterLocal((ctx, serverContext) => new CombatDamageResult
            {
                Success = true,
                Reason = serverContext.Label,
                Damage = 2,
            }, priority: 5);
        server.Hooks.On<CombatDamageContext, CrossResultContextA>(CrossResultContextA.Create)
            .RegisterLocal((ctx, serverContext) => new CombatDamageResult
            {
                Success = true,
                Reason = serverContext.Label,
                Damage = 1,
            }, priority: 5);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 50));

        Assert.Equal(2, result!.Value.Damage);
        Assert.Equal("b", result.Value.Reason);
    }
}

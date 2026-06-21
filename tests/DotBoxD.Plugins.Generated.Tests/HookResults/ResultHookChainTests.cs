using DotBoxD.Abstractions;

namespace DotBoxD.Plugins.Generated.Tests.HookResults;

public enum CombatRelation
{
    Pve = 0,
    Pvp = 1,
}

[Hook("combat.damage", typeof(CombatDamageResult))]
public sealed record CombatDamageContext(CombatRelation Relation, int Damage);

[HookResult]
public readonly partial record struct CombatDamageResult(bool Success, string? Reason, int Damage);

/// <summary>
/// End-to-end coverage for result-hook lowering: the <c>On&lt;TContext&gt;().Where(...).Register/RegisterLocal</c>
/// chains are authored as ordinary code, lowered by the DotBoxD generator loaded as a real build-time analyzer,
/// and intercepted into the live server. A passing FireAsync also proves interception ran — un-lowered, the
/// Register/RegisterLocal terminals throw.
/// </summary>
public sealed class ResultHookChainTests
{
    [Fact]
    public async Task Register_lowers_the_handler_and_returns_the_constructed_result()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .Register(ctx => new CombatDamageResult { Success = true, Damage = ctx.Damage * 2 }, priority: 100);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 50));

        Assert.NotNull(result);
        Assert.True(result!.Value.Success);
        Assert.Equal(100, result.Value.Damage);
    }

    [Fact]
    public async Task Register_lowers_the_fluent_builder_chain()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .Register(ctx => CombatDamageResult.Ok().WithDamage(ctx.Damage * 2), priority: 10);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 25));

        Assert.True(result!.Value.Success);
        Assert.Equal(50, result.Value.Damage);
    }

    [Fact]
    public async Task Register_reject_builder_abstains_to_the_next_handler()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Register(ctx => CombatDamageResult.Reject("nope"), priority: 100);
        server.Hooks.On<CombatDamageContext>()
            .Register(ctx => CombatDamageResult.Ok().WithDamage(1), priority: 0);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 25));

        Assert.True(result!.Value.Success);
        Assert.Equal(1, result.Value.Damage);
    }

    [Fact]
    public async Task Register_filter_that_does_not_match_yields_no_result()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pve)
            .Register(ctx => new CombatDamageResult { Success = true, Damage = ctx.Damage * 2 }, priority: 100);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pvp, 50));

        Assert.Null(result);
    }

    [Fact]
    public async Task Higher_priority_register_wins_over_lower()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Register(ctx => new CombatDamageResult { Success = true, Damage = 1 }, priority: 0);
        server.Hooks.On<CombatDamageContext>()
            .Register(ctx => new CombatDamageResult { Success = true, Damage = 999 }, priority: 100);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 50));

        Assert.Equal(999, result!.Value.Damage);
    }

    [Fact]
    public async Task Abstaining_register_falls_through_to_the_next()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        server.Hooks.On<CombatDamageContext>()
            .Register(ctx => new CombatDamageResult { Success = false, Reason = "abstain" }, priority: 100);
        server.Hooks.On<CombatDamageContext>()
            .Register(ctx => new CombatDamageResult { Success = true, Damage = 7 }, priority: 0);

        var result = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 50));

        Assert.True(result!.Value.Success);
        Assert.Equal(7, result.Value.Damage);
    }

    [Fact]
    public async Task RegisterLocal_runs_the_in_process_delegate_only_when_the_filter_matches()
    {
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var invoked = 0;
        server.Hooks.On<CombatDamageContext>()
            .Where(ctx => ctx.Relation == CombatRelation.Pvp)
            .RegisterLocal((ctx, hookContext) =>
            {
                invoked++;
                return new CombatDamageResult { Success = true, Damage = ctx.Damage + 1 };
            }, priority: 50);

        var miss = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pve, 10));
        Assert.Null(miss);
        Assert.Equal(0, invoked);

        var hit = await server.Hooks.FireAsync<CombatDamageContext, CombatDamageResult>(
            new CombatDamageContext(CombatRelation.Pvp, 10));
        Assert.Equal(11, hit!.Value.Damage);
        Assert.Equal(1, invoked);
    }
}

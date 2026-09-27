namespace DotBoxD.Kernels.Tests.Execution;

public sealed class PreparedPlanCacheLifetimeTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(8, false)]
    [InlineData(1, true)]
    [InlineData(8, true)]
    public async Task Idle_host_releases_unused_prepared_plans_and_modules(int count, bool execute)
    {
        using var host = PreparedPlanCacheFixture.CreateHost();
        var references = PreparedPlanCacheFixture.PrepareAndRelease(host, count, execute);
        await PreparedPlanCacheFixture.AssertCollectedAsync(references);
        GC.KeepAlive(host);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_identity_remains_usable_while_other_plans_are_collected(bool copy)
    {
        using var host = PreparedPlanCacheFixture.CreateHost();
        var holder = new PreparedPlanCacheFixture.PlanHolder();
        var retained = PreparedPlanCacheFixture.PrepareRetained(host, holder, copy);
        var released = PreparedPlanCacheFixture.PrepareAndRelease(host, 8, execute: false);
        await PreparedPlanCacheFixture.AssertCollectedAsync(released);

        PreparedPlanCacheFixture.ExecuteHeld(host, holder);
        holder.Plan = null;
        await PreparedPlanCacheFixture.AssertCollectedAsync(retained);
        GC.KeepAlive(host);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_preparation_preserves_each_live_plan(bool sameIdentity)
    {
        using var host = PreparedPlanCacheFixture.CreateHost();
        var requests = Enumerable.Range(0, 32).Select(i => Task.Run(() =>
            PreparedPlanCacheFixture.Prepare(host, sameIdentity ? "shared-identity" : $"identity-{i}")));
        var plans = await Task.WhenAll(requests);
        Assert.All(plans, plan => PreparedPlanCacheFixture.Execute(host, plan));
        Assert.Equal(sameIdentity ? 1 : 32, plans.Select(plan => plan.PlanHash).Distinct(StringComparer.Ordinal).Count());
    }
}

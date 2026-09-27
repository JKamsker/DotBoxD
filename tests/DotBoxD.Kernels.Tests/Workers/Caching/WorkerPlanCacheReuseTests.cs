using DotBoxD.Hosting;

namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerPlanCacheReuseTests
{
    [Theory]
    [InlineData("original")]
    [InlineData("copy")]
    [InlineData("equal-seal")]
    public void Equivalent_requests_reuse_the_prepared_worker_plan(string kind)
    {
        using var fixture = new WorkerPlanCacheFixture();
        var first = fixture.Prepare("shared");
        var next = kind switch
        {
            "copy" => WorkerPlanCacheFixture.Copy(first),
            "equal-seal" => fixture.Reprepare(first),
            _ => first
        };
        if (kind == "equal-seal")
        {
            Assert.NotSame(first.PlanSeal, next.PlanSeal);
            Assert.Equal(first.PlanSeal, next.PlanSeal);
        }
        fixture.Execute(first);
        var prepared = fixture.ObservedPlan();
        fixture.Execute(next);

        Assert.Same(prepared, fixture.ObservedPlan());
        Assert.Equal(1, fixture.FactoryCalls);
    }

    [Fact]
    public void Disposed_cache_does_not_retain_late_preparation_results()
    {
        using var fixture = new WorkerPlanCacheFixture();
        using var cache = new WorkerPreparedPlanCache();
        var plan = fixture.Prepare("late");
        cache.TryAdd(plan, plan);
        Assert.True(cache.TryGet(plan, out _));
        cache.Dispose();
        cache.TryAdd(plan, fixture.Reprepare(plan));

        Assert.False(cache.TryGet(plan, out _));
    }

    [Fact]
    public void Frequently_used_plan_survives_churn_and_evicted_plan_can_be_prepared_again()
    {
        using var fixture = new WorkerPlanCacheFixture();
        var cold = fixture.Prepare("cold");
        var hot = fixture.Prepare("hot");
        fixture.Execute(cold);
        var coldPrepared = fixture.ObservedPlan();
        fixture.Execute(hot);
        var hotPrepared = fixture.ObservedPlan();
        for (var i = 0; i < WorkerPlanCacheFixture.Capacity; i++)
        {
            fixture.Execute(fixture.Prepare($"churn-{i}"));
            fixture.Execute(hot);
            Assert.Same(hotPrepared, fixture.ObservedPlan());
        }

        fixture.Execute(cold);
        Assert.NotSame(coldPrepared, fixture.ObservedPlan());
        Assert.Equal(1, fixture.FactoryCalls);
    }
}

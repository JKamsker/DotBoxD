namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerPlanCacheLifetimeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task Live_worker_releases_evicted_plans_and_modules(int count)
    {
        using var fixture = new WorkerPlanCacheFixture();
        var references = Enumerable.Range(0, count)
            .SelectMany(i => fixture.ExecuteAndRelease($"released-{i}")).ToArray();
        fixture.FillCache();

        await WorkerPlanCacheFixture.AssertCollectedAsync(references);
        GC.KeepAlive(fixture);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task Disposed_worker_releases_cached_plans_and_modules(int count)
    {
        using var fixture = new WorkerPlanCacheFixture();
        var references = Enumerable.Range(0, count)
            .SelectMany(i => fixture.ExecuteAndRelease($"released-{i}")).ToArray();
        fixture.Worker.Dispose();

        await WorkerPlanCacheFixture.AssertCollectedAsync(references);
        GC.KeepAlive(fixture);
    }

    [Fact]
    public async Task Concurrent_requests_leave_a_bounded_cache()
    {
        using var fixture = new WorkerPlanCacheFixture();
        var requests = Enumerable.Range(0, WorkerPlanCacheFixture.Capacity * 2).Select(i => Task.Run(() =>
        {
            var plan = fixture.Prepare($"concurrent-{i}");
            fixture.Execute(plan);
            return new WeakReference(plan.Module);
        }));
        var references = await Task.WhenAll(requests);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.InRange(references.Count(reference => reference.IsAlive), 0, WorkerPlanCacheFixture.Capacity);
        Assert.Equal(1, fixture.FactoryCalls);
        GC.KeepAlive(fixture);
    }
}

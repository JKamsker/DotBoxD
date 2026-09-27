namespace DotBoxD.Kernels.Tests.Workers;

public sealed class WorkerHostLifetimeTests
{
    [Theory]
    [InlineData("unused")]
    [InlineData("initialized")]
    [InlineData("failed")]
    public async Task Disposed_worker_releases_factory_host_and_error(string state)
    {
        var probe = WorkerHostLifetimeFixture.CreateDisposed(state);
        using var worker = probe.Worker;
        await WorkerHostLifetimeFixture.AssertCollectedAsync(probe.References);
        GC.KeepAlive(worker);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Live_worker_preserves_its_factory_or_host(bool initialized)
    {
        var probe = WorkerHostLifetimeFixture.CreateLive(initialized);
        using var worker = probe.Worker;
        WorkerHostLifetimeFixture.Collect();
        Assert.True(probe.Reference.IsAlive);
        WorkerHostLifetimeFixture.Execute(worker, WorkerHostLifetimeFixture.Prepare());
        GC.KeepAlive(worker);
    }
}

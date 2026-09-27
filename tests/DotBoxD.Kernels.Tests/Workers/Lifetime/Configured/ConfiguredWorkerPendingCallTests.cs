namespace DotBoxD.Kernels.Tests.Workers;

public sealed class ConfiguredWorkerPendingCallTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_call_keeps_client_until_completion_after_host_disposal(bool fail)
    {
        var probe = ConfiguredWorkerLifetimeFixture.CreatePending();
        using var host = probe.Host;
        host.Dispose();
        host.Dispose();
        try
        {
            WorkerHostLifetimeFixture.Collect();
            Assert.True(probe.Client.IsAlive);
            Assert.False(probe.Execution.IsCompleted);
            Assert.False(probe.Gate.CancellationToken.IsCancellationRequested);
            Assert.Equal(0, probe.Counter.Disposals);
        }
        finally
        {
            probe.Gate.Complete(fail);
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(() => probe.Execution);
        await ConfiguredWorkerLifetimeFixture.AssertCollectedAsync([probe.Client]);
        Assert.Equal(1, probe.Counter.Calls);
        Assert.Equal(0, probe.Counter.Disposals);
        GC.KeepAlive(probe);
    }

    [Fact]
    public async Task Completing_a_call_preserves_a_live_hosts_client_until_disposal()
    {
        var probe = ConfiguredWorkerLifetimeFixture.CreatePending();
        using var host = probe.Host;
        try
        {
            WorkerHostLifetimeFixture.Collect();
            Assert.True(probe.Client.IsAlive);
        }
        finally
        {
            probe.Gate.Complete();
        }

        ConfiguredWorkerLifetimeFixture.AssertSuccess(await probe.Execution);
        WorkerHostLifetimeFixture.Collect();
        Assert.True(probe.Client.IsAlive);
        host.Dispose();
        await ConfiguredWorkerLifetimeFixture.AssertCollectedAsync([probe.Client]);
        Assert.Equal(0, probe.Counter.Disposals);
        GC.KeepAlive(probe);
    }
}

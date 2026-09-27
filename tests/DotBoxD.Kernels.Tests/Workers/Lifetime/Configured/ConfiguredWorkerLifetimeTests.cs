namespace DotBoxD.Kernels.Tests.Workers;

public sealed class ConfiguredWorkerLifetimeTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    public async Task Disposed_hosts_release_clients_even_when_completed_calls_are_retained(int count, bool execute)
    {
        var probe = ConfiguredWorkerLifetimeFixture.Create(count, execute, dispose: true);
        await ConfiguredWorkerLifetimeFixture.AssertCollectedAsync(probe.Clients);
        Assert.All(probe.Counters, counter => Assert.Equal(0, counter.Disposals));
        GC.KeepAlive(probe);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    public async Task Live_hosts_keep_clients_available(int count, bool execute)
    {
        var probe = ConfiguredWorkerLifetimeFixture.Create(count, execute);
        try
        {
            WorkerHostLifetimeFixture.Collect();
            Assert.All(probe.Clients, client => Assert.True(client.IsAlive));
            foreach (var host in probe.Hosts)
            {
                ConfiguredWorkerLifetimeFixture.AssertSuccess(await ConfiguredWorkerLifetimeFixture.Execute(host));
            }
            Assert.All(probe.Counters, counter => Assert.Equal(execute ? 2 : 1, counter.Calls));
            GC.KeepAlive(probe);
        }
        finally
        {
            foreach (var host in probe.Hosts)
            {
                host.Dispose();
            }
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    public async Task Dropped_hosts_release_clients(int count, bool execute)
    {
        var probe = ConfiguredWorkerLifetimeFixture.Create(count, execute, dispose: true, keepHosts: false);
        await ConfiguredWorkerLifetimeFixture.AssertCollectedAsync(probe.Clients);
        Assert.All(probe.Counters, counter => Assert.Equal(0, counter.Disposals));
        GC.KeepAlive(probe);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Disposing_one_host_preserves_caller_ownership_and_other_hosts(bool execute, bool concurrent)
    {
        var counter = new ConfiguredWorkerLifetimeFixture.Counter();
        using var worker = new ConfiguredWorkerLifetimeFixture.Worker(counter);
        using var first = ConfiguredWorkerLifetimeFixture.Host(worker);
        using var second = ConfiguredWorkerLifetimeFixture.Host(worker);
        if (execute)
        {
            ConfiguredWorkerLifetimeFixture.AssertSuccess(await ConfiguredWorkerLifetimeFixture.Execute(first));
        }
        if (concurrent)
        {
            Parallel.For(0, 8, _ => first.Dispose());
        }
        else
        {
            first.Dispose();
            first.Dispose();
        }

        Assert.Equal(0, counter.Disposals);
        ConfiguredWorkerLifetimeFixture.AssertSuccess(await ConfiguredWorkerLifetimeFixture.Execute(second));
        Assert.Equal(execute ? 2 : 1, counter.Calls);
        second.Dispose();
        Assert.Equal(0, counter.Disposals);
        worker.Dispose();
        Assert.Equal(1, counter.Disposals);
    }
}

namespace DotBoxD.Kernels.Tests.Audit;

public sealed class AuditObserverLifetimeTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    public async Task Retained_disposed_host_releases_its_observer_targets(int count, bool execute)
    {
        var probe = AuditObserverLifetimeFixture.Create(count, execute, dispose: true);
        using var host = probe.Host!;
        await AuditObserverLifetimeFixture.AssertCollectedAsync(probe.References);
        GC.KeepAlive(host);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Live_host_keeps_its_observers_available(int count)
    {
        var probe = AuditObserverLifetimeFixture.Create(count);
        using var host = probe.Host!;
        AuditObserverLifetimeFixture.Collect();
        Assert.All(probe.References, reference => Assert.True(reference.IsAlive));

        var result = AuditObserverLifetimeFixture.Execute(host);
        Assert.Equal(2, result.AuditEvents.Count);
        Assert.All(probe.References, reference => Assert.Equal(
            result.AuditEvents, Assert.IsType<AuditObserverLifetimeFixture.Observer>(reference.Target).Events));
        GC.KeepAlive(host);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Dropped_host_releases_its_observer_targets(int count)
    {
        var probe = AuditObserverLifetimeFixture.Create(count, dispose: true, keepHost: false);
        await AuditObserverLifetimeFixture.AssertCollectedAsync(probe.References);
    }

    [Fact]
    public void Host_disposal_preserves_the_callers_ownership_of_observer_objects()
    {
        using var observer = new AuditObserverLifetimeFixture.Observer();
        using var host = AuditObserverLifetimeFixture.Host(observer.OnEvent);
        var result = AuditObserverLifetimeFixture.Execute(host);
        host.Dispose();

        Assert.Equal(0, observer.DisposeCalls);
        observer.OnEvent(result.AuditEvents[0]);
        Assert.Equal(result.AuditEvents.Count + 1, observer.Events.Count);
    }
}

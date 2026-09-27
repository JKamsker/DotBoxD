using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Audit;

public sealed class AuditObserverPublicationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Observer_disposal_preserves_the_whole_publication(bool multicast, bool throwAfterDisposal)
    {
        SandboxHost? host = null;
        var firstEvents = new List<SandboxAuditEvent>();
        var secondEvents = new List<SandboxAuditEvent>();
        Action<SandboxAuditEvent> first = auditEvent =>
        {
            firstEvents.Add(auditEvent);
            host!.Dispose();
            if (throwAfterDisposal)
            {
                throw new InvalidOperationException("ordinary observer failure");
            }
        };
        Action<SandboxAuditEvent> second = secondEvents.Add;
        host = multicast
            ? AuditObserverLifetimeFixture.Host(first + second)
            : AuditObserverLifetimeFixture.Host(first, second);
        using var owned = host;

        var result = AuditObserverLifetimeFixture.Execute(host);
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Equal(2, result.AuditEvents.Count);
        Assert.Equal(result.AuditEvents, firstEvents);
        Assert.Equal(result.AuditEvents, secondEvents);
        Assert.Throws<ObjectDisposedException>(() => AuditObserverLifetimeFixture.Execute(host));
    }

    [Fact]
    public async Task Disposal_keeps_active_callbacks_alive_then_releases_their_targets()
    {
        await using var probe = BlockedAuditPublication.Start();
        Assert.True(probe.Entered.Wait(TimeSpan.FromSeconds(5)));
        probe.Host.Dispose();
        AuditObserverLifetimeFixture.Collect();
        Assert.True(probe.Observer.IsAlive);

        probe.Release.Set();
        var result = await probe.Execution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, result.AuditEvents.Count);
        Assert.Equal(result.AuditEvents, probe.Observed);
        await AuditObserverLifetimeFixture.AssertCollectedAsync(probe.Observer);
        GC.KeepAlive(probe.Host);
        GC.KeepAlive(probe.Execution);
    }
}

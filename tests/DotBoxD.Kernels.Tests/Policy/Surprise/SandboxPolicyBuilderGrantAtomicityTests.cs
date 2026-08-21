using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class SandboxPolicyBuilderGrantAtomicityTests
{
    [Fact]
    public void Grant_does_not_retain_state_when_configure_limits_throws()
    {
        var builder = SandboxPolicyBuilder.Create();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.Grant(
                "probe.read",
                new { },
                SandboxEffect.HostStateRead,
                _ => throw new InvalidOperationException("boom")));

        var policy = builder.Build();

        Assert.Equal("boom", exception.Message);
        Assert.DoesNotContain(policy.Grants, grant => grant.Id == "probe.read");
        Assert.False(policy.AllowedEffects.HasFlag(SandboxEffect.HostStateRead));
    }

    [Fact]
    public void Grant_retains_state_when_configure_limits_succeeds()
    {
        var policy = SandboxPolicyBuilder.Create()
            .Grant(
                "probe.read",
                new { },
                SandboxEffect.HostStateRead,
                limits => limits with { MaxFuel = 42 })
            .Build();

        Assert.Contains(policy.Grants, grant => grant.Id == "probe.read");
        Assert.True(policy.AllowedEffects.HasFlag(SandboxEffect.HostStateRead));
        Assert.Equal(42, policy.ResourceLimits.MaxFuel);
    }
}

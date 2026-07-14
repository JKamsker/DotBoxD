using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class SandboxPolicyLookupContractTests
{
    [Fact]
    public void GrantsCapability_rejects_null_capability_id()
    {
        var policy = CreatePolicy();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            policy.GrantsCapability(null!));

        Assert.Equal("capabilityId", exception.ParamName);
    }

    [Fact]
    public void GrantsCapability_with_clock_rejects_null_capability_id()
    {
        var policy = CreatePolicy();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            policy.GrantsCapability(null!, DateTimeOffset.UtcNow));

        Assert.Equal("capabilityId", exception.ParamName);
    }

    [Fact]
    public void TryGetGrant_rejects_null_capability_id()
    {
        var policy = CreatePolicy();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            policy.TryGetGrant(null!, out _));

        Assert.Equal("capabilityId", exception.ParamName);
    }

    [Fact]
    public void GetGrant_rejects_null_capability_id()
    {
        var policy = CreatePolicy();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            policy.GetGrant(null!));

        Assert.Equal("capabilityId", exception.ParamName);
    }

    [Fact]
    public void Unknown_non_null_capability_ids_remain_permission_misses()
    {
        var policy = CreatePolicy();

        Assert.False(policy.GrantsCapability("missing.capability"));
        Assert.False(policy.GrantsCapability("missing.capability", DateTimeOffset.UtcNow));
        Assert.False(policy.TryGetGrant("missing.capability", out _));

        var exception = Assert.Throws<SandboxRuntimeException>(() =>
            policy.GetGrant("missing.capability"));
        Assert.Equal(SandboxErrorCode.PermissionDenied, exception.Error.Code);
    }

    private static SandboxPolicy CreatePolicy()
        => SandboxPolicyBuilder.Create().GrantLogging().Build();
}

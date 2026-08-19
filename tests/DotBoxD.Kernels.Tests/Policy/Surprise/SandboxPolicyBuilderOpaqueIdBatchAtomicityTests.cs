using DotBoxD.Kernels.Policies;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class SandboxPolicyBuilderOpaqueIdBatchAtomicityTests
{
    [Fact]
    public void DeclareOpaqueIdTypes_rejects_invalid_name_without_retaining_prior_batch_names()
    {
        var builder = SandboxPolicyBuilder.Create();

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.DeclareOpaqueIdTypes(["TenantId", "String"]));

        Assert.Equal("name", exception.ParamName);
        Assert.Empty(builder.Build().DeclaredOpaqueIdTypes);
    }

    [Fact]
    public void DeclareOpaqueIdTypes_propagates_enumeration_failure_without_retaining_prior_batch_names()
    {
        var builder = SandboxPolicyBuilder.Create();

        Assert.Throws<SentinelException>(() => builder.DeclareOpaqueIdTypes(ValidNameThenThrows()));

        Assert.Empty(builder.Build().DeclaredOpaqueIdTypes);
    }

    [Fact]
    public void DeclareOpaqueIdTypes_retains_all_names_after_successful_batch()
    {
        var policy = SandboxPolicyBuilder.Create()
            .DeclareOpaqueIdTypes(["TenantId", "PlayerId"])
            .Build();

        Assert.Equal(["PlayerId", "TenantId"], policy.DeclaredOpaqueIdTypes.Order());
    }

    private static IEnumerable<string> ValidNameThenThrows()
    {
        yield return "TenantId";
        throw new SentinelException();
    }

    private sealed class SentinelException : Exception
    {
    }
}

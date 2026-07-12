using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class PolicyHashMalformedGrantTests
{
    [Fact]
    public void Hash_rejects_null_grant_entries_with_public_grants_error()
    {
        var policy = new SandboxPolicy(
            "null-grant-hash",
            SandboxEffects.Pure,
            [null!],
            new ResourceLimits());

        var exception = Assert.ThrowsAny<ArgumentException>(() => _ = policy.Hash);

        Assert.Equal(nameof(SandboxPolicy.Grants), exception.ParamName);
    }
}

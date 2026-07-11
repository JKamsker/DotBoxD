using DotBoxD.Hosting.Http.Policy;
using DotBoxD.Kernels.Policies;

namespace DotBoxD.Kernels.Tests.Runtime.Network.GrantValidation;

public sealed class SafeHttpGrantBuilderNullEntryTests
{
    [Fact]
    public void GrantHttpGet_rejects_null_allowed_host_entries()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            SandboxPolicyBuilder.Create().GrantHttpGet(
                [null!],
                maxResponseBytes: 1024));

        Assert.Equal("allowedHosts", exception.ParamName);
    }

    [Fact]
    public void GrantHttpGet_rejects_null_allowed_scheme_entries()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            SandboxPolicyBuilder.Create().GrantHttpGet(
                ["api.example.com"],
                maxResponseBytes: 1024,
                allowedSchemes: [null!]));

        Assert.Equal("allowedSchemes", exception.ParamName);
    }
}

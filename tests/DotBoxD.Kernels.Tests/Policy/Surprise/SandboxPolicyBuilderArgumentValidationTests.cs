using DotBoxD.Kernels.Policies;

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class SandboxPolicyBuilderArgumentValidationTests
{
    [Fact]
    public void Grant_rejects_null_capability_id()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            SandboxPolicyBuilder.Create().Grant(null!, new { }));

        Assert.Equal("capabilityId", exception.ParamName);
    }

    [Fact]
    public void Grant_rejects_null_parameters()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            SandboxPolicyBuilder.Create().Grant("custom.capability", null!));

        Assert.Equal("parameters", exception.ParamName);
    }

    [Fact]
    public void GrantFileRead_rejects_null_root()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            SandboxPolicyBuilder.Create().GrantFileRead(null!, 1024));

        Assert.Equal("root", exception.ParamName);
    }

    [Fact]
    public void GrantFileWrite_rejects_null_root()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            SandboxPolicyBuilder.Create().GrantFileWrite(
                null!,
                1024,
                allowCreate: true,
                allowOverwrite: true));

        Assert.Equal("root", exception.ParamName);
    }
}

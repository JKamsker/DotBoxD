using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class SandboxTypeContractTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Scalar_rejects_blank_type_names(string name)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            SandboxType.Scalar(name));

        AssertTypeNameParam(exception);
    }

    [Fact]
    public void Constructor_rejects_whitespace_type_names()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new SandboxType("   ", []));

        AssertTypeNameParam(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Init_rejects_blank_type_names(string name)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            SandboxType.I32 with { Name = name });

        AssertTypeNameParam(exception);
    }

    [Fact]
    public void Well_known_scalars_and_opaque_id_brands_remain_valid()
    {
        Assert.True(SandboxType.I32.IsKnown());
        Assert.True(SandboxType.String.IsKnown());

        var opaqueId = SandboxType.Scalar("PlayerId");

        Assert.True(opaqueId.IsKnown());
        Assert.True(SandboxType.IsKnownOpaqueId(opaqueId.Name));
    }

    private static void AssertTypeNameParam(ArgumentException exception)
    {
        Assert.NotNull(exception.ParamName);
        Assert.Contains("name", exception.ParamName, StringComparison.OrdinalIgnoreCase);
    }
}

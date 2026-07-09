namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts.Capability;

public sealed class CapabilityMetadataAttributeContractTests
{
    [Fact]
    public void CapabilityAttribute_rejects_null_id()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new CapabilityAttribute(null!));

        Assert.Equal("id", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CapabilityAttribute_rejects_blank_id(string id)
    {
        var exception = Assert.Throws<ArgumentException>(() => new CapabilityAttribute(id));

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void HostCapabilityAttribute_rejects_null_capability()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new HostCapabilityAttribute(null!, HostBindingEffect.HostStateRead));

        Assert.Equal("capability", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void HostCapabilityAttribute_rejects_blank_capability(string capability)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new HostCapabilityAttribute(capability, HostBindingEffect.HostStateRead));

        Assert.Equal("capability", exception.ParamName);
    }
}

namespace DotBoxD.Kernels.Tests.Policy;

public sealed class CapabilityGrantParameterMapTests
{
    [Fact]
    public void Capability_grant_initializer_rejects_null_parameter_maps()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new CapabilityGrant("log.write", new Dictionary<string, string>())
            {
                Parameters = null!
            });

        Assert.Equal("parameters", ex.ParamName);
    }

    [Fact]
    public void Capability_grant_with_expression_rejects_null_parameter_maps()
    {
        var grant = new CapabilityGrant("log.write", new Dictionary<string, string>());

        var ex = Assert.Throws<ArgumentNullException>(() =>
            grant with
            {
                Parameters = null!
            });

        Assert.Equal("parameters", ex.ParamName);
    }

    [Fact]
    public void Capability_grant_constructor_rejects_null_parameter_maps()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new CapabilityGrant("log.write", null!));

        Assert.Equal("parameters", ex.ParamName);
    }
}

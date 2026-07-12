namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

public sealed class PluginIdentityAttributeContractTests
{
    [Theory]
    [MemberData(nameof(MalformedPluginIdentityConstructors))]
    public void Identity_attribute_constructors_reject_malformed_non_null_ids(
        Func<Attribute> create)
    {
        var exception = Assert.Throws<ArgumentException>(() => create());

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void Plugin_attribute_null_id_remains_convention_default()
    {
        var attribute = new PluginAttribute(null);

        Assert.Null(attribute.Id);
    }

    [Fact]
    public void Server_extension_graft_constructor_null_id_remains_default()
    {
        var attribute = new ServerExtensionAttribute(typeof(ITestService), id: null);

        Assert.Null(attribute.Id);
        Assert.Equal(typeof(ITestService), attribute.Grafts);
    }

    public static TheoryData<Func<Attribute>> MalformedPluginIdentityConstructors()
        => new()
        {
            () => new PluginAttribute(""),
            () => new PluginAttribute("../bad"),
            () => new PluginAttribute("bad..id"),
            () => new ServerExtensionAttribute(""),
            () => new ServerExtensionAttribute("../bad"),
            () => new ServerExtensionAttribute("bad..id", typeof(ITestService)),
            () => new ServerExtensionAttribute(typeof(ITestService), "../bad"),
        };

    private interface ITestService;
}

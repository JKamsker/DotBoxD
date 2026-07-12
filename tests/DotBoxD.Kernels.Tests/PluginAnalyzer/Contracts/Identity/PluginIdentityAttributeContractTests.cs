namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

public sealed class PluginIdentityAttributeContractTests
{
    [Theory]
    [MemberData(nameof(MalformedPluginIdentityConstructors))]
    public void Identity_attribute_constructors_reject_malformed_non_null_ids(
        Func<Attribute> create,
        bool expectsStableIdMessage)
    {
        var exception = Assert.Throws<ArgumentException>(() => create());

        Assert.Equal("id", exception.ParamName);
        if (expectsStableIdMessage)
        {
            Assert.Contains("Id must be a stable identifier", exception.Message, StringComparison.Ordinal);
        }
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

    public static TheoryData<Func<Attribute>, bool> MalformedPluginIdentityConstructors()
        => new()
        {
            { () => new PluginAttribute(""), false },
            { () => new PluginAttribute("../bad"), true },
            { () => new PluginAttribute("bad..id"), true },
            { () => new ServerExtensionAttribute(""), false },
            { () => new ServerExtensionAttribute("../bad"), true },
            { () => new ServerExtensionAttribute("bad..id", typeof(ITestService)), true },
            { () => new ServerExtensionAttribute(typeof(ITestService), "../bad"), true },
        };

    private interface ITestService;
}

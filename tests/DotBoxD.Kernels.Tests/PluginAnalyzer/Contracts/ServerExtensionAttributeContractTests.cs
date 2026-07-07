namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

public sealed class ServerExtensionAttributeContractTests
{
    [Fact]
    public void Service_backed_constructor_rejects_null_id()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ServerExtensionAttribute(null!, typeof(IEchoService)));

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void Service_backed_constructor_rejects_null_service_type()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ServerExtensionAttribute("echo", null!));

        Assert.Equal("serviceType", exception.ParamName);
    }

    [Fact]
    public void Id_only_constructor_rejects_null_id()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ServerExtensionAttribute((string)null!));

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void Graft_constructor_rejects_null_graft_type()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ServerExtensionAttribute((Type)null!));

        Assert.Equal("grafts", exception.ParamName);
    }

    private interface IEchoService;
}

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

    [Fact]
    public void Receiver_client_constructor_rejects_null_receiver_type()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ServerExtensionClientAttribute(null!, "Echo"));

        Assert.Equal("receiverType", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Receiver_client_constructor_rejects_blank_name(string name)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ServerExtensionClientAttribute(typeof(IEchoService), name));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Receiver_method_constructor_rejects_blank_name(string name)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ServerExtensionMethodAttribute(typeof(IEchoService), name));

        Assert.Equal("name", exception.ParamName);
    }

    private interface IEchoService;
}

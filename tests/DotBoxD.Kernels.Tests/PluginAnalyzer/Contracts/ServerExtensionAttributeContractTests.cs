namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

public sealed class ServerExtensionAttributeContractTests
{
    [Theory]
    [InlineData("id")]
    [InlineData("serviceType")]
    public void Service_backed_constructor_rejects_null_required_metadata(string parameterName)
    {
        var exception = parameterName switch
        {
            "id" => Assert.Throws<ArgumentNullException>(
                () => new ServerExtensionAttribute(null!, typeof(IEchoService))),
            "serviceType" => Assert.Throws<ArgumentNullException>(
                () => new ServerExtensionAttribute("echo", null!)),
            _ => throw new ArgumentOutOfRangeException(nameof(parameterName), parameterName, null),
        };

        Assert.Equal(parameterName, exception.ParamName);
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

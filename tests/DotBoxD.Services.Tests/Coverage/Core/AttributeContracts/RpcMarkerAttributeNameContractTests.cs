using DotBoxD.Services.Attributes;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class RpcMarkerAttributeNameContractTests
{
    [Fact]
    public void RpcServiceAttribute_Name_AllowsOmittedAndValidValues()
    {
        Assert.Null(new RpcServiceAttribute().Name);

        var attribute = new RpcServiceAttribute { Name = "Game.World" };

        Assert.Equal("Game.World", attribute.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void RpcServiceAttribute_Name_RejectsBlankValues(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => new RpcServiceAttribute { Name = name });

        Assert.Equal("Name", ex.ParamName);
    }

    [Fact]
    public void RpcMethodAttribute_Name_AllowsOmittedAndValidValues()
    {
        Assert.Null(new RpcMethodAttribute().Name);

        var attribute = new RpcMethodAttribute { Name = "GetState" };

        Assert.Equal("GetState", attribute.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void RpcMethodAttribute_Name_RejectsBlankValues(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => new RpcMethodAttribute { Name = name });

        Assert.Equal("Name", ex.ParamName);
    }
}

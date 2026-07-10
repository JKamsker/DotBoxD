namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts.PluginServer;

public sealed class GeneratePluginServerAttributeContractTests
{
    [Fact]
    public void ContextFactory_allows_null_as_omitted_metadata()
    {
        var attribute = new GeneratePluginServerAttribute { ContextFactory = null };

        Assert.Null(attribute.ContextFactory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ContextFactory_rejects_blank_method_names(string contextFactory)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new GeneratePluginServerAttribute { ContextFactory = contextFactory });

        Assert.Equal("ContextFactory", exception.ParamName);
    }
}

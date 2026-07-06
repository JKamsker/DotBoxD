namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class SandboxHostBuilderArgumentValidationTests
{
    [Fact]
    public void UseCompilerCache_rejects_null_cache_directory_with_public_parameter_name()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<ArgumentNullException>(() => builder.UseCompilerCache(null!));

        Assert.Equal("cacheDirectory", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UseCompilerCache_rejects_blank_cache_directory_with_public_parameter_name(
        string cacheDirectory)
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<ArgumentException>(() => builder.UseCompilerCache(cacheDirectory));

        Assert.Equal("cacheDirectory", ex.ParamName);
    }
}

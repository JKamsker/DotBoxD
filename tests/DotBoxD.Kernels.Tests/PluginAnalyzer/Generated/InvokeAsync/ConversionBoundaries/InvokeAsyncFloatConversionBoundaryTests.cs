using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncFloatConversionBoundaryTests
{
    [Fact]
    public void Integral_to_float_conversion_fails_closed_instead_of_skipping_single_rounding()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<double> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    float rounded = 16_777_217;
                    return (double)rounded;
                });
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("local 'rounded'", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("int", "16_777_217", true)]
    [InlineData("long", "16_777_217L", true)]
    [InlineData("float", "16_777_217F", false)]
    public void Exact_double_widenings_remain_supported(
        string sourceType,
        string sourceValue,
        bool requiresConversion)
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource($$"""
            public static ValueTask<double> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    {{sourceType}} source = {{sourceValue}};
                    double widened = source;
                    return widened;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Equal(
            requiresConversion,
            source.Contains("numeric.toF64", StringComparison.Ordinal));
    }
}

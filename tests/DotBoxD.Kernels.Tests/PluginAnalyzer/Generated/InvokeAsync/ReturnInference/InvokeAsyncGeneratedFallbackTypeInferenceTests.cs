using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncGeneratedFallbackTypeInferenceTests
{
    [Theory]
    [InlineData("", "return ")]
    [InlineData("async ", "return await ")]
    public void Block_bodied_caller_supplies_the_unresolved_facade_return_type(
        string methodModifier,
        string returnPrefix)
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource($$"""
            public static {{methodModifier}}ValueTask<int> Run(RemotePluginServer kernels)
            {
                {{returnPrefix}}kernels.InvokeAsync(async world =>
                {
                    var health = world.GetHealth("monster-1");
                    return health;
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
        Assert.Contains("\\\"returnType\\\":\\\"I32\\\"", source, StringComparison.Ordinal);
    }
}

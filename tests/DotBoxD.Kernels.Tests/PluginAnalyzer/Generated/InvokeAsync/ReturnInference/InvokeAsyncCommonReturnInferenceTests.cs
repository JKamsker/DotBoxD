using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncCommonReturnInferenceTests
{
    [Fact]
    public void Mixed_numeric_returns_infer_common_long_type()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static async ValueTask<long> Run(RemotePluginServer kernels)
            {
                return await kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    if (world.GetHealth("monster-1") > 0)
                    {
                        return 1;
                    }

                    return 2L;
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("\\\"returnType\\\":\\\"I64\\\"", source, StringComparison.Ordinal);
        Assert.Contains("numeric.toI64", source, StringComparison.Ordinal);
    }
}

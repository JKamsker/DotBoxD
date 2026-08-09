using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncCapturedListKernelMethodExtensionRegressionTests
{
    [Fact]
    public void Captured_list_KernelMethod_extension_named_Add_is_not_treated_as_capture_mutation()
    {
        var staticCall = RunGeneratorAndAssertCompiles(Source("ListExtensions.Add(values, \"pure\");"));
        var extensionCall = RunGeneratorAndAssertCompiles(Source("values.Add(\"pure\");"));
        var generatedSource = string.Join("\n", extensionCall.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(staticCall.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain(extensionCall.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("list.add", generatedSource, StringComparison.Ordinal);
    }

    private static string Source(string invocation)
    {
        var source = UsageSource($$"""
            public static ValueTask<bool> Run(RemotePluginServer kernels, System.Collections.Generic.List<int> values)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    var health = world.GetHealth("monster-1");
                    {{invocation}}
                    return values.Count > health;
                });
            """);

        return source.Replace(
            "public static class Usage",
            """
            public static class ListExtensions
            {
                [KernelMethod]
                public static bool Add(this System.Collections.Generic.List<int> values, string marker)
                    => true;
            }

            public static class Usage
            """,
            StringComparison.Ordinal);
    }
}

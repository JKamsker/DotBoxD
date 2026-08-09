using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncListAddKernelMethodExtensionRegressionTests
{
    [Fact]
    public void List_receiver_KernelMethod_extension_named_Add_is_not_lowered_as_list_intrinsic()
    {
        var staticCall = RunGeneratorAndAssertCompiles(Source("ListExtensions.Add(values, \"extension\");"));
        var extensionCall = RunGeneratorAndAssertCompiles(Source("values.Add(\"extension\");"));
        var generatedSource = string.Join("\n", extensionCall.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(staticCall.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain(extensionCall.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("list.add", generatedSource, StringComparison.Ordinal);
    }

    private static string Source(string invocation)
    {
        var source = UsageSource($$"""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    System.Collections.Generic.List<int> values = new();
                    {{invocation}}
                    return world.GetHealth("monster-1");
                });
            """);

        return source.Replace(
            "public static class Usage",
            """
            public static class ListExtensions
            {
                [KernelMethod]
                public static int Add(this System.Collections.Generic.List<int> values, string tag)
                    => values.Count;
            }

            public static class Usage
            """,
            StringComparison.Ordinal);
    }
}

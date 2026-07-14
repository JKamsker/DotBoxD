using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGeneratedReceiverTestSources;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncGeneratedFacadeContextualReturnTests
{
    [Fact]
    public void Explicitly_typed_local_supplies_unresolved_facade_return_type()
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource("""
                public ValueTask<int> Probe()
                {
                    ValueTask<int> invocation = InvokeAsync(async world =>
                    {
                        return world.GetHealth("monster-1");
                    });
                    return invocation;
                }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
        Assert.Contains("\\\"returnType\\\":\\\"I32\\\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("(", ")")]
    [InlineData("", "!")]
    [InlineData("((", "))!")]
    public void Transparent_direct_return_supplies_unresolved_facade_return_type(
        string prefix,
        string suffix)
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource($$"""
                public ValueTask<int> Probe()
                {
                    return {{prefix}}InvokeAsync(async world =>
                    {
                        return world.GetHealth("monster-1");
                    }){{suffix}};
                }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
        Assert.Contains("\\\"returnType\\\":\\\"I32\\\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Incompatible_typed_local_context_remains_rejected()
    {
        var result = RunGenerator(GeneratedFacadeBodySource("""
                public ValueTask<string> Probe()
                {
                    ValueTask<string> invocation = InvokeAsync(async world =>
                    {
                        return world.GetHealth("monster-1");
                    });
                    return invocation;
                }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }
}

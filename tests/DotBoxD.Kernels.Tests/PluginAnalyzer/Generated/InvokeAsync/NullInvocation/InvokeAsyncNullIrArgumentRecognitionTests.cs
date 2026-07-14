using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGeneratedReceiverTestSources;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncNullIrArgumentRecognitionTests
{
    [Fact]
    public void Non_null_ir_argument_preserves_manual_ir_path()
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource("""
                public ValueTask<int> Probe(
                    IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int> irInvocation)
                    => InvokeAsync(
                        async (IGameWorldAccess world) =>
                        {
                            return world.GetHealth("monster-1");
                        },
                        irInvocation: irInvocation);
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(null)")]
    [InlineData("null!")]
    [InlineData("(IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int>?)null")]
    public void Null_like_ir_argument_still_generates_InvokeAsync_interceptor(string irExpression)
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource($$"""
                public ValueTask<int> Probe()
                    => InvokeAsync(
                        async (IGameWorldAccess world) =>
                        {
                            return world.GetHealth("monster-1");
                        },
                        irInvocation: {{irExpression}});
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }
}

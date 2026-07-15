using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncManualIrSurpriseTests
{
    [Fact]
    public void Named_delegate_with_explicit_manual_ir_is_not_lowered()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
            {
                Func<IGameWorldAccess, ValueTask<int>> lambda = async world =>
                {
                    return world.GetHealth("monster-1");
                };
                var manualIr = IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int>.FromGenerated(
                    "manual.ir",
                    static () => new object(),
                    static _ => Array.Empty<byte>(),
                    static (_, _) => 42);

                return kernels.InvokeAsync(lambda, manualIr);
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }
}

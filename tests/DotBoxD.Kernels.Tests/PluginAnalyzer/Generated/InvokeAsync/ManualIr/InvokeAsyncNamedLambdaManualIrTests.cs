using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncNamedLambdaManualIrTests
{
    [Fact]
    public void Named_capture_arguments_followed_by_positional_manual_ir_are_not_intercepted()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Capture
            {
                public string MonsterId { get; set; } = "";
            }

            public static ValueTask<int> Run(
                RemotePluginServer kernels,
                Capture captures,
                IRInvocation<Capture, RemoteServerInvocation<IGameWorldAccess, Capture, int>, int> irInvocation)
                => kernels.InvokeAsync(
                    captures: captures,
                    lambda: async (IGameWorldAccess world, Capture bag) =>
                    {
                        return world.GetHealth(bag.MonsterId);
                    },
                    irInvocation);
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Named_lambda_followed_by_positional_manual_ir_is_not_intercepted()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<int> Run(
                RemotePluginServer kernels,
                IRInvocation<Func<IGameWorldAccess, ValueTask<int>>, int> irInvocation)
                => kernels.InvokeAsync(
                    lambda: async (IGameWorldAccess world) =>
                    {
                        return world.GetHealth("monster-1");
                    },
                    irInvocation);
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }
}

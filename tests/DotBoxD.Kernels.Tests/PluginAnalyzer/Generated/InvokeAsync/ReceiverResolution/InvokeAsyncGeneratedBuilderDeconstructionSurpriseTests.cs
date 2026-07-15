using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncGeneratedBuilderDeconstructionSurpriseTests
{
    [Fact]
    public void Deconstructed_generated_builder_receiver_lowers_InvokeAsync()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<int> Run(
                DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService control)
            {
                var (server, ignored) = (RemotePluginServerBuilder.FromConnection(control).Build(), 0);
                _ = ignored;

                return server.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }
}

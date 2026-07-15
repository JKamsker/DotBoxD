using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncGeneratedBuilderCompositionSurpriseTests
{
    [Fact]
    public void Conditional_generated_builder_receiver_lowers_InvokeAsync()
    {
        AssertGeneratedReceiverLowers("""
            public static ValueTask<int> Run(
                DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService control,
                bool usePrimary)
                => (usePrimary
                    ? RemotePluginServerBuilder.FromConnection(control).Build()
                    : RemotePluginServerBuilder.FromConnection(control).Build()).InvokeAsync(
                        async (IGameWorldAccess world) =>
                        {
                            return world.GetHealth("monster-1");
                        });
            """);
    }

    [Fact]
    public void Coalesced_generated_builder_receiver_lowers_InvokeAsync()
    {
        AssertGeneratedReceiverLowers("""
            public static ValueTask<int> Run(
                DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService control)
                => (RemotePluginServerBuilder.FromConnection(control).Build()
                    ?? RemotePluginServerBuilder.FromConnection(control).Build()).InvokeAsync(
                        async (IGameWorldAccess world) =>
                        {
                            return world.GetHealth("monster-1");
                        });
            """);
    }

    [Fact]
    public void Typed_generated_facade_coalesced_with_builder_receiver_lowers_InvokeAsync()
    {
        AssertGeneratedReceiverLowers("""
            public static ValueTask<int> Run(
                DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService control,
                RemotePluginServer? existing)
                => (existing
                    ?? RemotePluginServerBuilder.FromConnection(control).Build()).InvokeAsync(
                        async (IGameWorldAccess world) =>
                        {
                            return world.GetHealth("monster-1");
                        });
            """);
    }

    private static void AssertGeneratedReceiverLowers(string usage)
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource(usage));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }
}

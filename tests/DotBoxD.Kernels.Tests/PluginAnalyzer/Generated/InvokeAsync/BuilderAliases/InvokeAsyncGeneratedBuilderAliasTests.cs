using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncGeneratedBuilderAliasTests
{
    [Fact]
    public void Static_imported_generated_builder_factory_lowers_InvokeAsync()
        => AssertBuilderReceiverLowers(
            "using static DotBoxD.Kernels.Game.Plugin.Client.RemotePluginServerBuilder;",
            "FromConnection(control)");

    [Fact]
    public void Type_aliased_generated_builder_lowers_InvokeAsync()
        => AssertBuilderReceiverLowers(
            "using Builder = DotBoxD.Kernels.Game.Plugin.Client.RemotePluginServerBuilder;",
            "Builder.FromConnection(control)");

    private static void AssertBuilderReceiverLowers(string usingDirective, string factoryExpression)
    {
        var source = usingDirective + Environment.NewLine + UsageSource($$"""
            public static ValueTask<int> Run(
                DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService control)
                => {{factoryExpression}}.Build().InvokeAsync(
                    async (IGameWorldAccess world) =>
                    {
                        return world.GetHealth("monster-1");
                    });
            """);

        var result = RunGeneratorAndAssertCompiles(source);
        var generatedSource = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", generatedSource, StringComparison.Ordinal);
    }
}

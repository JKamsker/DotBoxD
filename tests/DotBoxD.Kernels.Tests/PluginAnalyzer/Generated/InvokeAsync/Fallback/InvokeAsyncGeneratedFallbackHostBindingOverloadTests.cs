using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncGeneratedFallbackHostBindingOverloadTests
{
    [Theory]
    [InlineData("string", "host.read.string")]
    [InlineData("long", "host.read.long")]
    public void Implicit_world_call_selects_the_unique_best_host_binding_overload(
        string competingType,
        string competingBinding)
    {
        var result = RunGeneratorAndAssertCompiles(OverloadSource("1", competingType, competingBinding));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("host.read.int", source, StringComparison.Ordinal);
        Assert.DoesNotContain(competingBinding, source, StringComparison.Ordinal);
    }

    [Fact]
    public void Genuinely_ambiguous_host_binding_overloads_remain_rejected()
    {
        var result = RunGenerator(OverloadSource("default", "long", "host.read.long"));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("ambiguous", StringComparison.OrdinalIgnoreCase));
    }

    private static string OverloadSource(
        string argument,
        string competingType,
        string competingBinding)
        => UsageSource(
            $$"""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async world =>
                {
                    return world.Read({{argument}});
                });
            """,
            worldMembers: $$"""

                [HostBinding("host.read.int", "game.read", SandboxEffect.Cpu)]
                int Read(int value);

                [HostBinding("{{competingBinding}}", "game.read", SandboxEffect.Cpu)]
                int Read({{competingType}} value);
            """);
}

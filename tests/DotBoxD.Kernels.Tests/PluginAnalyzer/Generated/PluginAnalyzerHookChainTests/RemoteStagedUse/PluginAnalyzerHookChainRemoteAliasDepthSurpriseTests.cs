using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookChainRemoteAliasDepthSurpriseTests
{
    [Fact]
    public void Remote_staged_Run_through_seven_aliases_lowers()
    {
        var result = RunGenerator(RemoteStagedRunThroughAliases(aliasCount: 7));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "DBXK100" or "DBXK114");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("IRKernel.FromPackage", StringComparison.Ordinal));
    }

    [Fact]
    public void Remote_staged_Run_through_eight_aliases_lowers()
    {
        var result = RunGenerator(RemoteStagedRunThroughAliases(aliasCount: 8));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "DBXK100" or "DBXK114");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("IRKernel.FromPackage", StringComparison.Ordinal));
    }

    private static string RemoteStagedRunThroughAliases(int aliasCount)
    {
        var aliases = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, aliasCount)
                .Select(index => $"                    var alias{index} = {(index == 1 ? "staged" : $"alias{index - 1}")};"));

        return $$"""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
            {{aliases}}
                    alias{{aliasCount}}.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }
            }
            """;
    }
}

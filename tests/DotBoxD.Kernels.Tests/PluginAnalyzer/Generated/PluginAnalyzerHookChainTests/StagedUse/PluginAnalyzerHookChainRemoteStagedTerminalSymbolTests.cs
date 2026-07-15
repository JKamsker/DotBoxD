using Microsoft.CodeAnalysis;

using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookChainRemoteStagedTerminalSymbolTests
{
    [Fact]
    public void Remote_staged_user_extension_Run_overload_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class UserRunExtensions
            {
                public static int Run(this RemoteHookPipeline<DamageEvent> staged, int value) => value;
            }

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    staged.Run(42);
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("IRKernel.FromPackage", StringComparison.Ordinal));
    }

    [Fact]
    public void Remote_staged_real_Run_terminal_lowers()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    staged.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "DBXK100" or "DBXK114");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("IRKernel.FromPackage", StringComparison.Ordinal));
    }
}

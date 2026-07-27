using Microsoft.CodeAnalysis;

using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookChainConditionalAccessStagedUseTests
{
    [Fact]
    public void Remote_discarded_member_access_staged_hook_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var pipeline = hooks.On<DamageEvent>();
                    pipeline.Where(e => e.TargetId == "monster-1");
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("discarding", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("IRKernel.FromPackage", StringComparison.Ordinal));
    }

    [Fact]
    public void Remote_discarded_conditional_access_staged_hook_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    RemoteHookPipeline<DamageEvent>? pipeline = hooks.On<DamageEvent>();
                    pipeline?.Where(e => e.TargetId == "monster-1");
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("discarding", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("IRKernel.FromPackage", StringComparison.Ordinal));
    }
}

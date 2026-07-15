using Microsoft.CodeAnalysis;

using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookChainRemoteStagedUserExtensionTests
{
    [Fact]
    public void Remote_user_extension_Where_overload_does_not_report_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class UserRemotePipelineExtensions
            {
                public static string Where<TEvent>(this RemoteHookPipeline<TEvent> pipeline, int value)
                    => value.ToString();
            }

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    hooks.On<DamageEvent>().Where(42);
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
    }

    [Fact]
    public void Remote_discarded_DotBoxD_Where_still_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    hooks.On<DamageEvent>().Where(e => e.TargetId == "monster-1");
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("discarding", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}

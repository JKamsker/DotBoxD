using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Remote_assigned_staged_hook_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var pipeline = hooks.On<DamageEvent>();
                    pipeline = pipeline.Where(e => e.TargetId == "monster-1");
                    pipeline.Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("assigned", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_staged_Use_through_two_locals_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    var alias = staged;
                    alias.Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_staged_Select_from_alias_lowers()
    {
        const string source = """
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    staged.Select(e => e.TargetId)
                        .Run((targetId, ctx) => ctx.Messages.Send(targetId, "hit"));
                }
            }
            """;
        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK114");
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }
}

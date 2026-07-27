using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookChainGeneratorTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerHookChainRemoteNestedDiscardedStageTests
{
    [Fact]
    public void Remote_discarded_staged_hook_inside_tuple_initializer_reports_DBXK100()
    {
        var result = RunGenerator(RemoteHookUsageSource("""
            var pair = (hooks.On<DamageEvent>().Where(e => e.TargetId == "monster-1"), 0);
            _ = pair;
            """));

        AssertDiscardedStageDiagnostic(result);
    }

    [Fact]
    public void Remote_discarded_staged_hook_inside_array_initializer_reports_DBXK100()
    {
        var result = RunGenerator(RemoteHookUsageSource("""
            var stages = new[] { hooks.On<DamageEvent>().Where(e => e.TargetId == "monster-1") };
            _ = stages;
            """));

        AssertDiscardedStageDiagnostic(result);
    }

    [Fact]
    public void Remote_discarded_staged_hook_passed_to_helper_reports_DBXK100()
    {
        var result = RunGenerator(RemoteHookUsageSource("""
            Observe(hooks.On<DamageEvent>().Where(e => e.TargetId == "monster-1"));
            """));

        AssertDiscardedStageDiagnostic(result);
    }

    private static void AssertDiscardedStageDiagnostic(GeneratorDriverRunResult result)
    {
        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("discarding", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(result.GeneratedTrees);
    }

    private static string RemoteHookUsageSource(string configureBody)
        => $$"""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    {{configureBody}}
                }

                private static void Observe<T>(T value)
                {
                    _ = value;
                }
            }
            """;
}

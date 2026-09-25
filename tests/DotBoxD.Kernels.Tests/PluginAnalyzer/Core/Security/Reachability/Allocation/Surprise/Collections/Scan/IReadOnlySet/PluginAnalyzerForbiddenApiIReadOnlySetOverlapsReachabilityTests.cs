namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiIReadOnlySetOverlapsReachabilityTests
{
    [Fact]
    public async Task Reports_retained_read_only_set_overlaps_scan_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "IReadOnlySet<int>",
                "return Retained.Overlaps(Enumerable.Range(0, int.MaxValue));"),
            "DotBoxDPluginAnalyzerIReadOnlySetOverlapsReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.IReadOnlySet.Overlaps", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_direct_hash_set_overlaps_scan_control_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "HashSet<int>",
                "return Retained.Overlaps(Enumerable.Range(0, int.MaxValue));"),
            "DotBoxDPluginAnalyzerDirectHashSetOverlapsReachabilityControlTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.HashSet.Overlaps", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_read_only_set_contains_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "IReadOnlySet<int>",
                "return Retained.Contains(-1) && Retained.Count == 1;"),
            "DotBoxDPluginAnalyzerIReadOnlySetContainsAndCountControlTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string retainedType, string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("i-read-only-set-overlaps-reachability")]
                public sealed class IReadOnlySetOverlapsKernel : IEventKernel<string>
                {
                    private static readonly {{retainedType}} Retained = new HashSet<int> { -1 };

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{shouldHandleBody}}
                    }

                    public void Handle(string e, HookContext context)
                    {
                    }
                }
            }
            """;
}

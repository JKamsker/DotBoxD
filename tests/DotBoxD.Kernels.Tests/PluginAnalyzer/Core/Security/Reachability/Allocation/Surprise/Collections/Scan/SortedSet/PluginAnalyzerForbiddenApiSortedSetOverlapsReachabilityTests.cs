namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedSetOverlapsReachabilityTests
{
    [Fact]
    public async Task Reports_retained_sorted_set_overlaps_scan_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.Overlaps(Enumerable.Range(0, int.MaxValue));"),
            "DotBoxDPluginAnalyzerSortedSetOverlapsReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.SortedSet.Overlaps", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_sorted_set_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Add(-1);\n                        return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerSortedSetOverlapsAddCountControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("sorted-set-overlaps-reachability")]
                public sealed class SortedSetOverlapsKernel : IEventKernel<string>
                {
                    private static readonly SortedSet<int> Retained = [-1];

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

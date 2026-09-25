namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedSetIsSubsetOfReachabilityTests
{
    [Fact]
    public async Task Reports_sorted_set_is_subset_of_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("_ = Retained.IsSubsetOf(Enumerable.Range(0, int.MaxValue));"),
            "DotBoxDPluginAnalyzerSortedSetIsSubsetOfReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.SortedSet.IsSubsetOf",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_sorted_set_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Add(-1);
                _ = Retained.Count;
                """),
            "DotBoxDPluginAnalyzerSortedSetIsSubsetOfControlReachabilityTest");

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

                [Plugin("sorted-set-is-subset-of-reachability")]
                public sealed class SortedSetIsSubsetOfKernel : IEventKernel<string>
                {
                    private static readonly SortedSet<int> Retained = [-1];

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{shouldHandleBody}}
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

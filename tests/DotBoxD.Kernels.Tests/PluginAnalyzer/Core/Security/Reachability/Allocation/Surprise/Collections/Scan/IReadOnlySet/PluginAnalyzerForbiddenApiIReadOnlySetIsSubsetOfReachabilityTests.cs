namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiIReadOnlySetIsSubsetOfReachabilityTests
{
    [Fact]
    public async Task Reports_retained_set_is_subset_of_scan_through_IReadOnlySet_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "IReadOnlySet<int>",
                "_ = Retained.IsSubsetOf(Enumerable.Range(0, int.MaxValue));"),
            "DotBoxDPluginAnalyzerIReadOnlySetIsSubsetOfReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.IReadOnlySet.IsSubsetOf",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_direct_hash_set_is_subset_of_scan_control_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "HashSet<int>",
                "_ = Retained.IsSubsetOf(Enumerable.Range(0, int.MaxValue));"),
            "DotBoxDPluginAnalyzerDirectHashSetIsSubsetOfReachabilityControlTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.HashSet.IsSubsetOf",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_IReadOnlySet_contains_and_count_controls()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "IReadOnlySet<int>",
                """
                _ = Retained.Contains(-1);
                _ = Retained.Count;
                """),
            "DotBoxDPluginAnalyzerIReadOnlySetBoundedControlsReachabilityTest");

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

                [Plugin("i-read-only-set-is-subset-of-reachability")]
                public sealed class IReadOnlySetIsSubsetOfKernel : IEventKernel<string>
                {
                    private static readonly {{retainedType}} Retained = new HashSet<int> { -1 };

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{shouldHandleBody}}
                        return true;
                    }

                    public void Handle(string e, HookContext context)
                    {
                    }
                }
            }
            """;
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiISetIsProperSubsetOfReachabilityTests
{
    [Fact]
    public async Task Reports_retained_set_proper_subset_scan_through_ISet_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "ISet<int>",
                "return Retained.IsProperSubsetOf(Enumerable.Range(-1, int.MaxValue));"),
            "DotBoxDPluginAnalyzerISetIsProperSubsetOfReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.ISet.IsProperSubsetOf",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_direct_hash_set_proper_subset_scan_control_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "HashSet<int>",
                "return Retained.IsProperSubsetOf(Enumerable.Range(-1, int.MaxValue));"),
            "DotBoxDPluginAnalyzerDirectHashSetIsProperSubsetOfReachabilityControlTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.HashSet.IsProperSubsetOf",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_retained_ISet_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("ISet<int>", "Retained.Add(-1);\n                    return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerISetIsProperSubsetOfAddCountControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string retainedType, string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("i-set-is-proper-subset-of-reachability")]
                public sealed class ISetIsProperSubsetOfKernel : IEventKernel<string>
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

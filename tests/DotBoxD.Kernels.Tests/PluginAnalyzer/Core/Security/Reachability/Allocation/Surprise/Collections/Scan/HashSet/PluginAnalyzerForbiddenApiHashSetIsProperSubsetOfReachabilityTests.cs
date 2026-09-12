namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashSetIsProperSubsetOfReachabilityTests
{
    [Fact]
    public async Task Reports_retained_hash_set_proper_subset_scan_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.IsProperSubsetOf(Enumerable.Range(-1, int.MaxValue));"),
            "DotBoxDPluginAnalyzerHashSetIsProperSubsetOfReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.HashSet.IsProperSubsetOf",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_retained_hash_set_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Add(-1);\n                    return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerHashSetIsProperSubsetOfAddCountControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("hash-set-is-proper-subset-of-reachability")]
                public sealed class HashSetIsProperSubsetOfKernel : IEventKernel<string>
                {
                    private static readonly HashSet<int> Retained = [-1];

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

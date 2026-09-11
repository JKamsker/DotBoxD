namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashSetRemoveWhereReachabilityTests
{
    [Fact]
    public async Task Reports_retained_hash_set_remove_where_predicate_scan_in_reachable_event_kernel()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("_ = Retained.RemoveWhere(static _ => false);"),
            "DotBoxDPluginAnalyzerHashSetRemoveWhereReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.HashSet.RemoveWhere", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_retained_hash_set_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("_ = Retained.Count;"),
            "DotBoxDPluginAnalyzerHashSetAddAndCountReachabilityControlTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string additionalHandleStatement)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("hash-set-remove-where-reachability")]
                public sealed class HashSetRemoveWhereKernel : IEventKernel<int>
                {
                    private static readonly HashSet<int> Retained = new();

                    public bool ShouldHandle(int e, HookContext context) => true;

                    public void Handle(int e, HookContext context)
                    {
                        Retained.Add(e);
                        {{additionalHandleStatement}}
                    }
                }
            }
            """;
}

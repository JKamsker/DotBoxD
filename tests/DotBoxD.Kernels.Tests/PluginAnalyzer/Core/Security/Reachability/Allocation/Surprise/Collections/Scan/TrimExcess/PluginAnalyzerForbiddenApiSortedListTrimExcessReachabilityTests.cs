namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedListTrimExcessReachabilityTests
{
    [Fact]
    public async Task Reports_retained_sorted_list_trim_excess_in_reachable_event_kernel()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.TrimExcess();"),
            "DotBoxDPluginAnalyzerSortedListTrimExcessReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.SortedList.TrimExcess", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_retained_sorted_list_add_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(string.Empty),
            "DotBoxDPluginAnalyzerSortedListAddReachabilityControlTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string additionalShouldHandleStatement)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("sorted-list-trim-excess-reachability")]
                public sealed class SortedListTrimExcessKernel : IEventKernel<string>
                {
                    private static readonly SortedList<int, string> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(Retained.Count, e);
                        {{additionalShouldHandleStatement}}
                        return true;
                    }

                    public void Handle(string e, HookContext context)
                    {
                    }
                }
            }
            """;
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableAverageReachabilityTests
{
    [Fact]
    public async Task Reports_retained_list_selector_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Enumerable.Average(Retained, static value => value) > 0;"),
            "DotBoxDPluginAnalyzerEnumerableAverageReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.Average", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerEnumerableAverageBoundedControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string returnStatement)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("enumerable-average-reachability")]
                public sealed class EnumerableAverageKernel : IEventKernel<string>
                {
                    private readonly List<int> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(e.Length);
                        {{returnStatement}}
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

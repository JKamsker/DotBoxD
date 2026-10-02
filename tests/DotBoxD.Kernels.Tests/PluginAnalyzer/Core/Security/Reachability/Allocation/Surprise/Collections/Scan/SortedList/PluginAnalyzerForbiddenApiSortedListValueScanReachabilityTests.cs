namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedListValueScanReachabilityTests
{
    [Theory]
    [InlineData(
        "_ = Retained.ContainsValue(\"missing\");",
        "System.Collections.Generic.SortedList.ContainsValue")]
    [InlineData(
        "_ = Retained.IndexOfValue(\"missing\");",
        "System.Collections.Generic.SortedList.IndexOfValue")]
    public async Task Reports_retained_sorted_list_value_scan_in_reachable_event_kernel(
        string shouldHandleStatement,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(shouldHandleStatement),
            "DotBoxDPluginAnalyzerSortedListValueScanReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(expectedApi, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_retained_sorted_list_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                _ = Retained.Count;
                """),
            "DotBoxDPluginAnalyzerSortedListValueScanControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("sorted-list-value-scan-reachability")]
                public sealed class SortedListValueScanKernel : IEventKernel<string>
                {
                    private static readonly SortedList<int, string> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(Retained.Count, e);
                        {{shouldHandleBody}}
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

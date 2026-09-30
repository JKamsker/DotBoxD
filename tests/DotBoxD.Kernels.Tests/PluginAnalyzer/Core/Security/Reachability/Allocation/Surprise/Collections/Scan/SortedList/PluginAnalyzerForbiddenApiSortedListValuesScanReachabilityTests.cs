namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedListValuesScanReachabilityTests
{
    [Theory]
    [InlineData("Retained.Values.Contains(\"missing\")", "System.Collections.Generic.ICollection.Contains")]
    [InlineData("Retained.Values.IndexOf(\"missing\") >= 0", "System.Collections.Generic.IList.IndexOf")]
    public async Task Reports_retained_sorted_list_values_scan_in_reachable_event_kernel(
        string valueLookup,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(valueLookup),
            "DotBoxDPluginAnalyzerSortedListValuesScanReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(expectedApi, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_retained_sorted_list_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Count > 0"),
            "DotBoxDPluginAnalyzerSortedListValuesScanAddCountControlTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string returnExpression)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("sorted-list-values-scan-reachability")]
                public sealed class SortedListValuesScanKernel : IEventKernel<string>
                {
                    private static readonly SortedList<int, string> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(Retained.Count, e);
                        return {{returnExpression}};
                    }

                    public void Handle(string e, HookContext context)
                    {
                    }
                }
            }
            """;
}

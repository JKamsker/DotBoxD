namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableGroupByReachabilityTests
{
    [Fact]
    public async Task Reports_retained_iterator_grouping_materialization_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Enumerable.GroupBy(Enumerate(), static value => value % 2).GetEnumerator().MoveNext();"),
            "DotBoxDPluginAnalyzerEnumerableGroupByReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.GroupBy", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_and_count_controls()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerEnumerableGroupByBoundedControlTest");

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

                [Plugin("enumerable-group-by-reachability")]
                public sealed class EnumerableGroupByKernel : IEventKernel<string>
                {
                    private readonly List<int> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(e.Length);
                        {{returnStatement}}
                    }

                    public void Handle(string e, HookContext context) { }

                    private IEnumerable<int> Enumerate()
                    {
                        foreach (var value in Retained)
                        {
                            yield return value;
                        }
                    }
                }
            }
            """;
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableSkipLastReachabilityTests
{
    [Fact]
    public async Task Reports_retained_iterator_buffering_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Enumerable.SkipLast(Enumerate(), int.MaxValue).GetEnumerator().MoveNext();"),
            "DotBoxDPluginAnalyzerEnumerableSkipLastReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.SkipLast", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_and_count_controls()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerEnumerableSkipLastBoundedControlReachabilityTest");

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

                [Plugin("enumerable-skip-last-reachability")]
                public sealed class EnumerableSkipLastKernel : IEventKernel<string>
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
                        foreach (var item in Retained)
                        {
                            yield return item;
                        }
                    }
                }
            }
            """;
}

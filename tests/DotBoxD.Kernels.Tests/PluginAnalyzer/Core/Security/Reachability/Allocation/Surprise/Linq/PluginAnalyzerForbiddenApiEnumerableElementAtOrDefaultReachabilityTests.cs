namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableElementAtOrDefaultReachabilityTests
{
    [Fact]
    public async Task Reports_retained_iterator_scan_via_static_enumerable_element_at_or_default()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Enumerable.ElementAtOrDefault(Enumerate(), Retained.Count - 1) >= 0;"),
            "DotBoxDPluginAnalyzerEnumerableElementAtOrDefaultReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.ElementAtOrDefault", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerEnumerableElementAtOrDefaultBoundedControlTest");

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

                [Plugin("enumerable-element-at-or-default-reachability")]
                public sealed class EnumerableElementAtOrDefaultKernel : IEventKernel<string>
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

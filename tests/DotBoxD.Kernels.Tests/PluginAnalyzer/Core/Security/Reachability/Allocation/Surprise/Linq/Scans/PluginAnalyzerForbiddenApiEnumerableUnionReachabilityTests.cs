namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableUnionReachabilityTests
{
    [Fact]
    public async Task Reports_retained_duplicate_iterator_scan_via_static_enumerable_union()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("""
                using var enumerator = Enumerable.Union(Enumerable.Repeat(0, 1), Enumerate()).GetEnumerator();
                _ = enumerator.MoveNext();
                return enumerator.MoveNext();
                """),
            "DotBoxDPluginAnalyzerEnumerableUnionReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.Union", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerEnumerableUnionBoundedControlTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string body)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("enumerable-union-reachability")]
                public sealed class EnumerableUnionKernel : IEventKernel<string>
                {
                    private readonly List<int> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(0);
                        {{body}}
                    }

                    public void Handle(string e, HookContext context) { }

                    private IEnumerable<int> Enumerate()
                    {
                        foreach (var _ in Retained)
                        {
                            yield return 0;
                        }
                    }
                }
            }
            """;
}

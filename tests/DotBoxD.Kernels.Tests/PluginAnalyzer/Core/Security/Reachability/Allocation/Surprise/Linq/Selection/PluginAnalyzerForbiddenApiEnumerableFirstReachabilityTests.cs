namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableFirstReachabilityTests
{
    [Fact]
    public async Task Reports_retained_list_predicate_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                try
                {
                    return Enumerable.First(Retained, static _ => false) == 0;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
                """),
            "DotBoxDPluginAnalyzerEnumerableFirstReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.First", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_direct_list_contains_scan_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.Contains(-1);"),
            "DotBoxDPluginAnalyzerEnumerableFirstListContainsControlReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.List.Contains", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_retained_list_first_without_predicate()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Enumerable.First(Retained) == 0;"),
            "DotBoxDPluginAnalyzerEnumerableFirstWithoutPredicateReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerEnumerableFirstBoundedControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string body)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("enumerable-first-reachability")]
                public sealed class EnumerableFirstKernel : IEventKernel<string>
                {
                    private readonly List<int> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(e.Length);
                        {{body}}
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableLastReachabilityTests
{
    [Fact]
    public async Task Reports_retained_list_predicate_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("""
                try
                {
                    return Enumerable.Last(Retained, static _ => false) > 0;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
                """),
            "DotBoxDPluginAnalyzerEnumerableLastReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.Last", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_count_and_parameterless_last_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.Count > 0 && Retained.Last() > 0;"),
            "DotBoxDPluginAnalyzerEnumerableLastBoundedControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_predicate_method_group_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("""
                Func<IEnumerable<int>, Func<int, bool>, int> last = Enumerable.Last;
                try
                {
                    return last(Retained, static _ => false) > 0;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
                """),
            "DotBoxDPluginAnalyzerEnumerableLastMethodGroupReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.Last", diagnostic.GetMessage(), StringComparison.Ordinal);
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

                [Plugin("enumerable-last-reachability")]
                public sealed class EnumerableLastKernel : IEventKernel<string>
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

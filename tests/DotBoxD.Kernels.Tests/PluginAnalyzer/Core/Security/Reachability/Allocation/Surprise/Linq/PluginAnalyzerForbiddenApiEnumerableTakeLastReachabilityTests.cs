namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableTakeLastReachabilityTests
{
    [Fact]
    public async Task Reports_retained_iterator_take_last_buffering_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("""
                using var values = Enumerable.TakeLast(Enumerate(), int.MaxValue).GetEnumerator();
                return values.MoveNext();
                """),
            "DotBoxDPluginAnalyzerEnumerableTakeLastReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.TakeLast", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerEnumerableTakeLastBoundedControlReachabilityTest");

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

                [Plugin("enumerable-take-last-reachability")]
                public sealed class EnumerableTakeLastKernel : IEventKernel<string>
                {
                    private readonly List<int> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(e.Length);
                        {{body}}
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

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableSkipWhileReachabilityTests
{
    [Fact]
    public async Task Reports_retained_linked_list_prefix_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerEnumerableSkipWhileReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.SkipWhile", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source()
        => """
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("enumerable-skip-while-reachability")]
                public sealed class EnumerableSkipWhileKernel : IEventKernel<string>
                {
                    private readonly LinkedList<int> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.AddLast(0);
                        var enumerator = Enumerable.SkipWhile(Retained, static value => value == 0).GetEnumerator();
                        return enumerator.MoveNext();
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

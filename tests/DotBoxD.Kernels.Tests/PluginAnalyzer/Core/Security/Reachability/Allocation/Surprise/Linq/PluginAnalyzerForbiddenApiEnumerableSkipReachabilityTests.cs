namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableSkipReachabilityTests
{
    [Fact]
    public async Task Reports_retained_linked_list_skip_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerEnumerableSkipReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.Skip", diagnostic.GetMessage(), StringComparison.Ordinal);
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

                [Plugin("enumerable-skip-reachability")]
                public sealed class EnumerableSkipKernel : IEventKernel<string>
                {
                    private readonly LinkedList<int> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.AddLast(e.Length);
                        var enumerator = Enumerable.Skip(Retained, int.MaxValue).GetEnumerator();
                        return enumerator.MoveNext();
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

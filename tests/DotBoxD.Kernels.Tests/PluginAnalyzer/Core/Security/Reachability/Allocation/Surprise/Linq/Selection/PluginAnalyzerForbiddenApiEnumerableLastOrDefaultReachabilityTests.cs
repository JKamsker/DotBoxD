namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableLastOrDefaultReachabilityTests
{
    [Fact]
    public async Task Reports_retained_list_predicate_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Enumerable.LastOrDefault(Retained, static _ => false) == 0;"),
            "DotBoxDPluginAnalyzerEnumerableLastOrDefaultReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.LastOrDefault", diagnostic.GetMessage(), StringComparison.Ordinal);
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

                [Plugin("enumerable-last-or-default-reachability")]
                public sealed class EnumerableLastOrDefaultKernel : IEventKernel<string>
                {
                    private readonly List<int> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(e.Length);
                        {{returnStatement}}
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableSingleOrDefaultReachabilityTests
{
    [Fact]
    public async Task Reports_retained_list_predicate_scan_with_bounded_list_controls()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerEnumerableSingleOrDefaultReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.SingleOrDefault", diagnostic.GetMessage(), StringComparison.Ordinal);
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

                [Plugin("enumerable-single-or-default-reachability")]
                public sealed class EnumerableSingleOrDefaultKernel : IEventKernel<string>
                {
                    private readonly List<int> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(e.Length);
                        return Retained.Count >= 0 &&
                            Enumerable.SingleOrDefault(Retained, static _ => false) == 0;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableSingleReachabilityTests
{
    [Fact]
    public async Task Reports_retained_list_predicate_scan_in_handled_exception_path()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerEnumerableSingleReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.Single", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source()
        => """
            #nullable enable

            namespace Sample
            {
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("enumerable-single-reachability")]
                public sealed class EnumerableSingleKernel : IEventKernel<string>
                {
                    private readonly List<int> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(e.Length);
                        var retainedCount = Retained.Count;
                        try
                        {
                            return Enumerable.Single(Retained, static _ => false) == retainedCount;
                        }
                        catch (InvalidOperationException)
                        {
                            return false;
                        }
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

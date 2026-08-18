namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedSetEnumerableConstructionReachabilityTests
{
    [Fact]
    public async Task Reports_reachable_sorted_set_enumerable_construction_but_allows_empty_constructor()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerSortedSetEnumerableConstructionReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.SortedSet", diagnostic.GetMessage(), StringComparison.Ordinal);
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

                [Plugin("sorted-set-enumerable-construction")]
                public sealed class SortedSetEnumerableConstructionKernel : IEventKernel<string>
                {
                    private static readonly SortedSet<byte> Retained = new(Enumerable.Repeat((byte)0, int.MaxValue));
                    private static readonly SortedSet<byte> Empty = new();

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

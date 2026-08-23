namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedListComparerConstructorReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_sorted_list_dictionary_and_comparer_constructor()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new SortedList<int, int>(source, Comparer<int>.Default)"),
            "DotBoxDPluginAnalyzerSortedListComparerConstructorReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.SortedList", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_sorted_list_comparer_only_constructor()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new SortedList<int, int>(Comparer<int>.Default)"),
            "DotBoxDPluginAnalyzerSortedListComparerOnlyConstructorReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string initializer)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("sorted-list-comparer-constructor-reachability")]
                public sealed class SortedListComparerConstructorKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context)
                    {
                        var source = new Dictionary<int, int>();
                        var retained = {{initializer}};
                    }
                }
            }
            """;
}

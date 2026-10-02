namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedDictionaryContainsValueReachabilityTests
{
    [Fact]
    public async Task Reports_sorted_dictionary_contains_value_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained[e] = "value";
                _ = Retained.ContainsValue("missing");
                """),
            "DotBoxDPluginAnalyzerSortedDictionaryContainsValueReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.SortedDictionary.ContainsValue",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_sorted_dictionary_insert_and_count()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained[e] = "value";
                _ = Retained.Count;
                """),
            "DotBoxDPluginAnalyzerSortedDictionaryInsertAndCountReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("sorted-dictionary-contains-value-reachability")]
                public sealed class SortedDictionaryContainsValueKernel : IEventKernel<int>
                {
                    private static readonly SortedDictionary<int, string> Retained = new();

                    public bool ShouldHandle(int e, HookContext context)
                    {
                        {{shouldHandleBody}}
                        return true;
                    }

                    public void Handle(int e, HookContext context) { }
                }
            }
            """;
}

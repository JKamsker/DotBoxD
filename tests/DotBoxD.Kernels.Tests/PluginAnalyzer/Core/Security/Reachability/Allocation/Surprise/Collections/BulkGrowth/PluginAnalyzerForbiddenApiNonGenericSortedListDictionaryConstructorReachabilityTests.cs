namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiNonGenericSortedListDictionaryConstructorReachabilityTests
{
    [Theory]
    [InlineData("new System.Collections.SortedList(source)")]
    [InlineData("new System.Collections.SortedList(source, System.Collections.Comparer.Default)")]
    public async Task Reports_non_generic_sorted_list_dictionary_copy_constructor(string construction)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(construction),
            "DotBoxDPluginAnalyzerNonGenericSortedListDictionaryConstructorReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.SortedList", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_non_generic_sorted_list_comparer_only_constructor()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new System.Collections.SortedList(System.Collections.Comparer.Default)"),
            "DotBoxDPluginAnalyzerNonGenericSortedListComparerConstructorReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string construction)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("non-generic-sorted-list-dictionary-constructor-reachability")]
                public sealed class NonGenericSortedListDictionaryConstructorKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context)
                    {
                        var source = new Hashtable();
                        _ = Build(source);
                    }

                    private static SortedList Build(IDictionary source) => {{construction}};
                }
            }
            """;
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiOrderedDictionaryCapacityReachabilityTests
{
    [Fact]
    public async Task Reports_capacity_realized_by_ordered_dictionary_collection_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "private static readonly OrderedDictionary Retained = new(int.MaxValue) { { \"key\", \"value\" } };"),
            "DotBoxDPluginAnalyzerOrderedDictionaryInitializerCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Specialized.OrderedDictionary",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_lazy_ordered_dictionary_capacity_without_insertion()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly OrderedDictionary Retained = new(int.MaxValue);"),
            "DotBoxDPluginAnalyzerLazyOrderedDictionaryCapacityReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string fieldDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Specialized;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("ordered-dictionary-capacity-reachability")]
                public sealed class OrderedDictionaryCapacityKernel : IEventKernel<string>
                {
                    {{fieldDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;
                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableToFrozenDictionaryReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_enumerable_to_frozen_dictionary_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerEnumerableToFrozenDictionaryReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.ToFrozenDictionary", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source()
        => """
            #nullable enable

            namespace Sample
            {
                using System.Collections.Frozen;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("enumerable-to-frozen-dictionary-reachability")]
                public sealed class EnumerableToFrozenDictionaryKernel : IEventKernel<string>
                {
                    private static readonly FrozenDictionary<int, int> Retained = Enumerable.Range(0, int.MaxValue).ToFrozenDictionary(static value => value);

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

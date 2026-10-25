namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiImmutableDictionaryToImmutableDictionaryReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_immutable_dictionary_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerImmutableDictionaryToImmutableDictionaryReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Immutable.ImmutableDictionary.ToImmutableDictionary",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    private static string Source()
        => """
            #nullable enable

            namespace Sample
            {
                using System.Collections.Immutable;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("immutable-dictionary-to-immutable-dictionary-reachability")]
                public sealed class ImmutableDictionaryToImmutableDictionaryKernel : IEventKernel<string>
                {
                    private static readonly ImmutableDictionary<int, int> Retained = Enumerable.Range(0, int.MaxValue).ToImmutableDictionary(static value => value);

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

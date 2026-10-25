namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableToLookupReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_enumerable_to_lookup_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerEnumerableToLookupReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.ToLookup", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source()
        => """
            #nullable enable

            namespace Sample
            {
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("enumerable-to-lookup-reachability")]
                public sealed class EnumerableToLookupKernel : IEventKernel<string>
                {
                    private static readonly ILookup<int, int> Retained = Enumerable.Range(0, int.MaxValue).ToLookup(static value => value);

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

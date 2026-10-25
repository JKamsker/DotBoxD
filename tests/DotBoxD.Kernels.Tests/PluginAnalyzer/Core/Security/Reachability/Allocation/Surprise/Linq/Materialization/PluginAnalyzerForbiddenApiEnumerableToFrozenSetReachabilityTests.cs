namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableToFrozenSetReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_enumerable_to_frozen_set_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerEnumerableToFrozenSetReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Frozen.FrozenSet.ToFrozenSet", diagnostic.GetMessage(), StringComparison.Ordinal);
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

                [Plugin("enumerable-to-frozen-set-reachability")]
                public sealed class EnumerableToFrozenSetKernel : IEventKernel<string>
                {
                    private static readonly FrozenSet<int> Retained = Enumerable.Range(0, int.MaxValue).ToFrozenSet();

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

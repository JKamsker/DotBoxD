namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableToHashSetReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_enumerable_to_hash_set_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(),
            "DotBoxDPluginAnalyzerEnumerableToHashSetReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Linq.Enumerable.ToHashSet", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source()
        => """
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("enumerable-to-hash-set-reachability")]
                public sealed class EnumerableToHashSetKernel : IEventKernel<string>
                {
                    private static readonly HashSet<byte> Bounded = new() { 0 };
                    private static readonly HashSet<byte> Retained = Enumerable.Repeat((byte)0, int.MaxValue).ToHashSet();

                    public bool ShouldHandle(string e, HookContext context) => Bounded.Contains(0);

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashSetUnionWithReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_hash_set_union_with_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Add(e);
                Retained.UnionWith(Enumerable.Range(0, int.MaxValue));
                """),
            "DotBoxDPluginAnalyzerHashSetUnionWithReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.HashSet.UnionWith",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_hash_set_add_and_count_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Add(e);
                _ = Retained.Count;
                """),
            "DotBoxDPluginAnalyzerHashSetAddAndCountReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("hash-set-union-with-reachability")]
                public sealed class HashSetUnionWithKernel : IEventKernel<int>
                {
                    private static readonly HashSet<int> Retained = new();

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

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashSetExceptWithReachabilityTests
{
    [Fact]
    public async Task Reports_unmetered_hash_set_except_with_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Add(e);
                Retained.ExceptWith(Enumerable.Range(0, int.MaxValue));
                """),
            "DotBoxDPluginAnalyzerHashSetExceptWithReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.HashSet.ExceptWith",
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

        Assert.Empty(diagnostics);
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

                [Plugin("hash-set-except-with-reachability")]
                public sealed class HashSetExceptWithKernel : IEventKernel<int>
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

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiISetOverlapsReachabilityTests
{
    [Fact]
    public async Task Reports_retained_set_overlaps_scan_through_ISet_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "ISet<int>",
                "return Retained.Overlaps(Enumerable.Range(0, int.MaxValue));"),
            "DotBoxDPluginAnalyzerISetOverlapsReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.ISet.Overlaps", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_direct_hash_set_overlaps_scan_control_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "HashSet<int>",
                "return Retained.Overlaps(Enumerable.Range(0, int.MaxValue));"),
            "DotBoxDPluginAnalyzerDirectHashSetOverlapsReachabilityControlTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.HashSet.Overlaps", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source(string retainedType, string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("i-set-overlaps-reachability")]
                public sealed class ISetOverlapsKernel : IEventKernel<string>
                {
                    private static readonly {{retainedType}} Retained = new HashSet<int> { -1 };

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{shouldHandleBody}}
                    }

                    public void Handle(string e, HookContext context)
                    {
                    }
                }
            }
            """;
}

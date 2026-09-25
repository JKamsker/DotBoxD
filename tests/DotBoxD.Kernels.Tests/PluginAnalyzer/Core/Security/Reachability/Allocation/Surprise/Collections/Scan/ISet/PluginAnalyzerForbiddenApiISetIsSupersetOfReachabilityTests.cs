namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiISetIsSupersetOfReachabilityTests
{
    [Fact]
    public async Task Reports_retained_set_is_superset_of_scan_through_ISet_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.IsSupersetOf(Retained.Select(static value => value));"),
            "DotBoxDPluginAnalyzerISetIsSupersetOfReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.ISet.IsSupersetOf",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_direct_hash_set_is_superset_of_scan_control_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "return Retained.IsSupersetOf(Retained.Select(static value => value));",
                "HashSet<int>"),
            "DotBoxDPluginAnalyzerDirectHashSetIsSupersetOfReachabilityControlTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.HashSet.IsSupersetOf",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_retained_ISet_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerISetIsSupersetOfAddAndCountControlTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody, string retainedType = "ISet<int>")
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("i-set-is-superset-of-reachability")]
                public sealed class ISetIsSupersetOfKernel : IEventKernel<int>
                {
                    private static readonly {{retainedType}} Retained = new HashSet<int>();

                    public bool ShouldHandle(int e, HookContext context)
                    {
                        {{shouldHandleBody}}
                    }

                    public void Handle(int e, HookContext context)
                    {
                        Retained.Add(e);
                    }
                }
            }
            """;
}

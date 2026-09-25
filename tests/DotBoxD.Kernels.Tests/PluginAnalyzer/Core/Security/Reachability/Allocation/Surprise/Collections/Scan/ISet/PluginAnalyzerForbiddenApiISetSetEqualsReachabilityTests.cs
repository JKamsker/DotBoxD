namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiISetSetEqualsReachabilityTests
{
    [Fact]
    public async Task Reports_retained_set_set_equals_scan_through_ISet_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "ISet<byte>",
                "Retained.Add(e);\n                    return Retained.SetEquals(Retained.Select(static value => value));"),
            "DotBoxDPluginAnalyzerISetSetEqualsReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.ISet.SetEquals", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_direct_hash_set_set_equals_scan_control_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "HashSet<byte>",
                "Retained.Add(e);\n                    return Retained.SetEquals(Retained.Select(static value => value));"),
            "DotBoxDPluginAnalyzerDirectHashSetSetEqualsReachabilityControlTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.HashSet.SetEquals", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_retained_set_add_and_count_control_through_ISet()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("ISet<byte>", "Retained.Add(e);\n                    return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerISetSetEqualsAddCountControlTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string retainedType, string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("i-set-set-equals-reachability")]
                public sealed class ISetSetEqualsKernel : IEventKernel<byte>
                {
                    private static readonly {{retainedType}} Retained = new HashSet<byte>();

                    public bool ShouldHandle(byte e, HookContext context)
                    {
                        {{shouldHandleBody}}
                    }

                    public void Handle(byte e, HookContext context) { }
                }
            }
            """;
}

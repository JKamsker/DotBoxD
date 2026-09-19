namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiIReadOnlySetSetEqualsReachabilityTests
{
    [Fact]
    public async Task Reports_retained_read_only_set_set_equals_scan_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "IReadOnlySet<byte>",
                "Backing.Add(e);\n                    return Retained.SetEquals(Retained.Select(static value => value));"),
            "DotBoxDPluginAnalyzerIReadOnlySetSetEqualsReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.IReadOnlySet.SetEquals", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_direct_hash_set_set_equals_scan_control_in_reachable_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "HashSet<byte>",
                "Backing.Add(e);\n                    return Retained.SetEquals(Retained.Select(static value => value));"),
            "DotBoxDPluginAnalyzerDirectHashSetSetEqualsReachabilityControlTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.HashSet.SetEquals", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_read_only_set_contains_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("IReadOnlySet<byte>", "return Retained.Contains(e) && Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerIReadOnlySetContainsAndCountControlTest");

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

                [Plugin("i-read-only-set-set-equals-reachability")]
                public sealed class IReadOnlySetSetEqualsKernel : IEventKernel<byte>
                {
                    private static readonly HashSet<byte> Backing = new HashSet<byte>();
                    private static readonly {{retainedType}} Retained = Backing;

                    public bool ShouldHandle(byte e, HookContext context)
                    {
                        {{shouldHandleBody}}
                    }

                    public void Handle(byte e, HookContext context) { }
                }
            }
            """;
}

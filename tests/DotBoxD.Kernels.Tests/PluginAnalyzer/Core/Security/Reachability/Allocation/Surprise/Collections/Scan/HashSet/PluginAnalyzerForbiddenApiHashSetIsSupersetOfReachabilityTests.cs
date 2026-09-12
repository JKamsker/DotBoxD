namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashSetIsSupersetOfReachabilityTests
{
    [Fact]
    public async Task Reports_retained_hash_set_is_superset_of_scan_in_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Add(e);\n                    return Retained.IsSupersetOf(Retained.Select(static value => value));"),
            "DotBoxDPluginAnalyzerHashSetIsSupersetOfReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.HashSet.IsSupersetOf",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_retained_hash_set_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Add(e);\n                    return Retained.Count > 0;"),
            "DotBoxDPluginAnalyzerHashSetIsSupersetOfAddControlTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("hash-set-is-superset-of-reachability")]
                public sealed class HashSetIsSupersetOfKernel : IEventKernel<byte>
                {
                    private static readonly HashSet<byte> Retained = [];

                    public bool ShouldHandle(byte e, HookContext context)
                    {
                        {{shouldHandleBody}}
                    }

                    public void Handle(byte e, HookContext context) { }
                }
            }
            """;
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListTrueForAllReachabilityTests
{
    [Fact]
    public async Task Reports_list_true_for_all_callback_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Add(0);
                Retained.TrueForAll(static _ => true);
                """),
            "DotBoxDPluginAnalyzerListTrueForAllReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.List.TrueForAll",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Add(0);"),
            "DotBoxDPluginAnalyzerListTrueForAllAddReachabilityControl");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string shouldHandleStatements)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-true-for-all-reachability")]
                public sealed class ListTrueForAllKernel : IEventKernel<string>
                {
                    private static readonly List<byte> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{shouldHandleStatements}}
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

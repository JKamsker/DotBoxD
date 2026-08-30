namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListFindLastReachabilityTests
{
    [Fact]
    public async Task Reports_retained_list_find_last_predicate_scan_in_reachable_event_kernel()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("_ = Retained.FindLast(static _ => false);"),
            "DotBoxDPluginAnalyzerListFindLastReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.List.FindLast", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_retained_list_add_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(string.Empty),
            "DotBoxDPluginAnalyzerListAddReachabilityControlTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string additionalHandleStatement)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-find-last-reachability")]
                public sealed class ListFindLastKernel : IEventKernel<string>
                {
                    private static readonly List<byte> Retained = new();

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context)
                    {
                        Retained.Add(0);
                        {{additionalHandleStatement}}
                    }
                }
            }
            """;
}

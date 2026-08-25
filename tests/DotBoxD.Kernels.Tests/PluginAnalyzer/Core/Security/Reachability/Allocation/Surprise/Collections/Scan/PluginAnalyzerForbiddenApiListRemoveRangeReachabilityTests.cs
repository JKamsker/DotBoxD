namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListRemoveRangeReachabilityTests
{
    [Fact]
    public async Task Reports_list_remove_range_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("""
                Retained.Add(0);
                Retained.RemoveRange(0, Retained.Count);
                """),
            "DotBoxDPluginAnalyzerListRemoveRangeReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.List.RemoveRange",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Add(0);"),
            "DotBoxDPluginAnalyzerBoundedListAddReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string handleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-remove-range-reachability")]
                public sealed class ListRemoveRangeKernel : IEventKernel<string>
                {
                    private static readonly List<byte> Retained = new();

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context)
                    {
                        {{handleBody}}
                    }
                }
            }
            """;
}

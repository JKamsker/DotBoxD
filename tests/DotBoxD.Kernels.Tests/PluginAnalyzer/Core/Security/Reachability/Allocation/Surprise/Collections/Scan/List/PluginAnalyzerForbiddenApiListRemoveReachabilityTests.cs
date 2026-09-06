namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListRemoveReachabilityTests
{
    [Fact]
    public async Task Reports_retained_list_remove_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Remove((byte)255);"),
            "DotBoxDPluginAnalyzerListRemoveReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.List.Remove", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("_ = Retained.Count;"),
            "DotBoxDPluginAnalyzerListRemoveAddCountControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string operation)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-remove-reachability")]
                public sealed class ListRemoveKernel : IEventKernel<string>
                {
                    private static readonly List<byte> Retained = new();

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context)
                    {
                        Retained.Add(0);
                        {{operation}}
                    }
                }
            }
            """;
}

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListLastIndexOfReachabilityTests
{
    [Theory]
    [InlineData("return Retained.LastIndexOf((byte)255) >= 0;")]
    [InlineData("return Retained.LastIndexOf((byte)255, 0) >= 0;")]
    [InlineData("return Retained.LastIndexOf((byte)255, 0, 1) >= 0;")]
    public async Task Reports_retained_list_reverse_scan_in_reachable_event_handler(string scan)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(scan),
            "DotBoxDPluginAnalyzerListLastIndexOfReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.List.LastIndexOf", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return true;"),
            "DotBoxDPluginAnalyzerListLastIndexOfAddControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string returnStatement)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-last-index-of-reachability")]
                public sealed class ListLastIndexOfKernel : IEventKernel<string>
                {
                    private readonly List<byte> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(0);
                        {{returnStatement}}
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

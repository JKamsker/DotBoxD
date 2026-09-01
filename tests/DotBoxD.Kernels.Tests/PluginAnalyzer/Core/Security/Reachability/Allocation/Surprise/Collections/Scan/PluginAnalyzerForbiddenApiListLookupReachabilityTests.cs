namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListLookupReachabilityTests
{
    [Theory]
    [InlineData(
        "return Retained.IndexOf((byte)255) >= 0;",
        "System.Collections.Generic.List.IndexOf")]
    [InlineData(
        "return Retained.Contains((byte)255);",
        "System.Collections.Generic.List.Contains")]
    public async Task Reports_retained_list_lookup_scan_in_reachable_event_handler(
        string lookup,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(lookup),
            "DotBoxDPluginAnalyzerListLookupReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(expectedApi, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return true;"),
            "DotBoxDPluginAnalyzerListLookupAddControlReachabilityTest");

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

                [Plugin("list-lookup-reachability")]
                public sealed class ListLookupKernel : IEventKernel<string>
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

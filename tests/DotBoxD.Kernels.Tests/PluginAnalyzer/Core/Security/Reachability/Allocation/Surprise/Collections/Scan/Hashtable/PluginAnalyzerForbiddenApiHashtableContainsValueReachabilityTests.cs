namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashtableContainsValueReachabilityTests
{
    [Fact]
    public async Task Reports_retained_hashtable_contains_value_scan_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return Retained.ContainsValue(\"missing\");"),
            "DotBoxDPluginAnalyzerHashtableContainsValueReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Hashtable.ContainsValue",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("return Retained.Contains(\"missing\");")]
    [InlineData("return Retained.ContainsKey(\"missing\");")]
    [InlineData("return Retained.Count > 0;")]
    public async Task Does_not_report_bounded_hashtable_lookup_or_count_control(string returnStatement)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(returnStatement),
            "DotBoxDPluginAnalyzerHashtableContainsValueControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string returnStatement)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("hashtable-contains-value-reachability")]
                public sealed class HashtableContainsValueKernel : IEventKernel<string>
                {
                    private readonly Hashtable Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained[e] = e;
                        {{returnStatement}}
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

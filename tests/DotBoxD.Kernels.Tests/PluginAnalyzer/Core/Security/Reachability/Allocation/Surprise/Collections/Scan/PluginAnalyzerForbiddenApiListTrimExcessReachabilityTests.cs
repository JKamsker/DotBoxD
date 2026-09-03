namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListTrimExcessReachabilityTests
{
    [Fact]
    public async Task Reports_retained_list_trim_excess_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.TrimExcess(); return true;"),
            "DotBoxDPluginAnalyzerListTrimExcessReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.List.TrimExcess", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return true;"),
            "DotBoxDPluginAnalyzerListTrimExcessAddControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string body)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-trim-excess-reachability")]
                public sealed class ListTrimExcessKernel : IEventKernel<string>
                {
                    private readonly List<string> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        Retained.Add(e);
                        {{body}}
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

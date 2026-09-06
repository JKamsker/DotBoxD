namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListClearReachabilityTests
{
    [Fact]
    public async Task Reports_retained_list_clear_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "Retained.Add(e);\n        if (Retained.Count > 1)\n        {\n            Retained.Clear();\n        }"),
            "DotBoxDPluginAnalyzerListClearReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.List.Clear", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_retained_list_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Add(e);\n        _ = Retained.Count;"),
            "DotBoxDPluginAnalyzerListClearAddAndCountControlTest");

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

                [Plugin("list-clear-reachability")]
                public sealed class ListClearKernel : IEventKernel<string>
                {
                    private static readonly List<string> Retained = [];

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context)
                    {
                        {{handleBody}}
                    }
                }
            }
            """;
}

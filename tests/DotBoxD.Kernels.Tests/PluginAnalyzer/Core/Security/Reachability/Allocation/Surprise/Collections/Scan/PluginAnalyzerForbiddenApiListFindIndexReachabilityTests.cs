namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListFindIndexReachabilityTests
{
    [Theory]
    [InlineData("Retained.FindIndex(static _ => false);")]
    [InlineData("Retained.FindIndex(0, static _ => false);")]
    [InlineData("Retained.FindIndex(0, Retained.Count, static _ => false);")]
    public async Task Reports_list_find_index_callback_in_reachable_event_kernel(string findIndex)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source($"Retained.Add(0); {findIndex}"),
            "DotBoxDPluginAnalyzerListFindIndexReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.List.FindIndex", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_in_reachable_event_kernel()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("Retained.Add(0);"),
            "DotBoxDPluginAnalyzerListAddReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-find-index-reachability")]
                public sealed class ListFindIndexKernel : IEventKernel<string>
                {
                    private static readonly List<byte> Retained = [];

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{shouldHandleBody}}
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

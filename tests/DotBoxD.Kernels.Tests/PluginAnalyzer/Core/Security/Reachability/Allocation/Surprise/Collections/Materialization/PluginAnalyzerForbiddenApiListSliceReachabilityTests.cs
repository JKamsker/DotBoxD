namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListSliceReachabilityTests
{
    [Fact]
    public async Task Reports_list_slice_materialization_in_reachable_helper()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return source.Slice(0, source.Count);"),
            "DotBoxDPluginAnalyzerListSliceReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.List.Slice", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_non_materializing_list_count_helper()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("return source.Count;"),
            "DotBoxDPluginAnalyzerListSliceCountReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string helperBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-slice-reachability")]
                public sealed class ListSliceKernel : IEventKernel<string>
                {
                    private static object Materialize(List<byte> source)
                    {
                        {{helperBody}}
                    }

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

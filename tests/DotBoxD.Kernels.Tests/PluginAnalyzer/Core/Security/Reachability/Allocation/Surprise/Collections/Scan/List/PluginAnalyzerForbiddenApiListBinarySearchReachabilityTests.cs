namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListBinarySearchReachabilityTests
{
    [Theory]
    [InlineData("Retained.BinarySearch((byte)0);")]
    [InlineData("Retained.BinarySearch((byte)0, Comparer<byte>.Default);")]
    [InlineData("Retained.BinarySearch(0, Retained.Count, (byte)0, Comparer<byte>.Default);")]
    public async Task Reports_list_binary_search_scan_in_reachable_event_handler(string search)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source($$"""
                Retained.Add(0);
                {{search}}
                """),
            "DotBoxDPluginAnalyzerListBinarySearchReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.List.BinarySearch",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_list_add_and_count_control()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Add(0);
                _ = Retained.Count;
                """),
            "DotBoxDPluginAnalyzerListBinarySearchControlReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string handlerBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-binary-search-reachability")]
                public sealed class ListBinarySearchKernel : IEventKernel<string>
                {
                    private static readonly List<byte> Retained = [];

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context)
                    {
                        {{handlerBody}}
                    }
                }
            }
            """;
}

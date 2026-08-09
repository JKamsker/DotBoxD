namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiSortedListCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "SortedList<string, string> capacity",
        "private static readonly System.Collections.Generic.SortedList<string, string> Retained = new(int.MaxValue);",
        "System.Collections.Generic.SortedList")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Retained = System.IO.File.Exists(\"plugin-sorted-list.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_sorted_list_capacity_static_initializer(
        string testCase,
        string fieldDeclaration,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(fieldDeclaration),
            "DotBoxDPluginAnalyzerSortedListCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains(expectedApi, StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    [Fact]
    public async Task Allows_parameterless_sorted_list_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly System.Collections.Generic.SortedList<string, string> Retained = new();"),
            "DotBoxDPluginAnalyzerParameterlessSortedListReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string fieldDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("sorted-list-capacity-reachability")]
                public sealed class SortedListCapacityKernel : IEventKernel<string>
                {
                    {{fieldDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

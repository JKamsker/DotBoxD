namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListRangeGrowthReachabilityTests
{
    [Theory]
    [InlineData(
        "AddRange",
        "retained.AddRange(Enumerable.Repeat((byte)0, int.MaxValue));",
        "System.Collections.Generic.List.AddRange",
        true)]
    [InlineData(
        "InsertRange",
        "retained.InsertRange(0, Enumerable.Repeat((byte)0, int.MaxValue));",
        "System.Collections.Generic.List.InsertRange",
        true)]
    [InlineData(
        "Add control",
        "retained.Add((byte)0);",
        "",
        false)]
    public async Task Reports_unbounded_list_range_growth_in_reachable_helper(
        string testCase,
        string growthCall,
        string expectedApi,
        bool expectsDiagnostic)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(growthCall),
            "DotBoxDPluginAnalyzerListRangeGrowthReachabilityTest");

        var securityDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001").ToArray();

        if (!expectsDiagnostic)
        {
            Assert.Empty(securityDiagnostics);
            return;
        }

        var diagnostic = Assert.Single(securityDiagnostics);
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    private static string Source(string growthCall)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-range-growth-reachability")]
                public sealed class ListRangeGrowthKernel : IEventKernel<string>
                {
                    private static readonly List<byte> Retained = CreateRetainedList();

                    private static List<byte> CreateRetainedList()
                    {
                        var retained = new List<byte>();
                        {{growthCall}}
                        return retained;
                    }

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

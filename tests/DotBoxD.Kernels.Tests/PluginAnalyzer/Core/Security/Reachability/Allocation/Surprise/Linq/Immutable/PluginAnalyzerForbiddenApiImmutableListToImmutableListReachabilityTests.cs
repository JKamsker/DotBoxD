namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiImmutableListToImmutableListReachabilityTests
{
    [Theory]
    [InlineData(
        "ImmutableList ToImmutableList materialization",
        "private static readonly System.Collections.Immutable.ImmutableList<int> Retained = System.Collections.Immutable.ImmutableList.ToImmutableList(System.Linq.Enumerable.Range(0, int.MaxValue));",
        "System.Collections.Immutable.ImmutableList.ToImmutableList")]
    [InlineData(
        "System.IO positive control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_immutable_list_materialization_static_initializer(
        string testCase,
        string memberDeclaration,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(memberDeclaration),
            "DotBoxDPluginAnalyzerImmutableListToImmutableListReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    private static string Source(string memberDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("immutable-list-to-immutable-list-static-initializer")]
                public sealed class ImmutableListToImmutableListKernel : IEventKernel<string>
                {
                    {{memberDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

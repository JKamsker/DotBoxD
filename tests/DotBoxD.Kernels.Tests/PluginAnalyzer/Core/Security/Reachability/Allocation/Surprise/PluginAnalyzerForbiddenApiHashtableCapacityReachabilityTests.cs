namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashtableCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "Hashtable capacity",
        "private static readonly System.Collections.Hashtable Retained = new(int.MaxValue);",
        "System.Collections.Hashtable")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Retained = System.IO.File.Exists(\"plugin-hashtable.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_hashtable_capacity_static_initializer(
        string testCase,
        string fieldDeclaration,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(fieldDeclaration),
            "DotBoxDPluginAnalyzerHashtableCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains(expectedApi, StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    [Fact]
    public async Task Allows_parameterless_hashtable_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly System.Collections.Hashtable Retained = new();"),
            "DotBoxDPluginAnalyzerParameterlessHashtableReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string fieldDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("hashtable-capacity-reachability")]
                public sealed class HashtableCapacityKernel : IEventKernel<string>
                {
                    {{fieldDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

}

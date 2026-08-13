namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiCollectionsUtilHashtableCapacityReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_collections_util_hashtable_capacity_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "private static readonly System.Collections.Hashtable Retained = " +
                "System.Collections.Specialized.CollectionsUtil.CreateCaseInsensitiveHashtable(int.MaxValue);"),
            "DotBoxDPluginAnalyzerCollectionsUtilHashtableCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains("CollectionsUtil.CreateCaseInsensitiveHashtable", StringComparison.Ordinal) ||
            message.Contains("System.Collections.Hashtable", StringComparison.Ordinal),
            message);
    }

    [Fact]
    public async Task Allows_parameterless_collections_util_hashtable_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "private static readonly System.Collections.Hashtable Retained = " +
                "System.Collections.Specialized.CollectionsUtil.CreateCaseInsensitiveHashtable();"),
            "DotBoxDPluginAnalyzerParameterlessCollectionsUtilHashtableReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string fieldDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("collections-util-hashtable-capacity-reachability")]
                public sealed class CollectionsUtilHashtableCapacityKernel : IEventKernel<string>
                {
                    {{fieldDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

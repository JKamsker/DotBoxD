namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashtableDictionaryConstructionReachabilityTests
{
    [Theory]
    [InlineData("dictionary copy", "new(Source)")]
    [InlineData("dictionary copy with load factor", "new(Source, 0.75f)")]
    [InlineData("dictionary copy with load factor and comparer", "new(Source, 0.75f, null)")]
    public async Task Reports_hashtable_dictionary_copy_static_initializer(
        string testCase,
        string construction)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(construction),
            "DotBoxDPluginAnalyzerHashtableDictionaryConstructionReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains("System.Collections.Hashtable", StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    [Fact]
    public async Task Allows_parameterless_hashtable_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new()"),
            "DotBoxDPluginAnalyzerParameterlessHashtableDictionaryConstructionReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string construction)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("hashtable-dictionary-construction-reachability")]
                public sealed class HashtableDictionaryConstructionKernel : IEventKernel<string>
                {
                    private static readonly System.Collections.IDictionary Source = new System.Collections.Hashtable();
                    private static readonly System.Collections.Hashtable Retained = {{construction}};

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

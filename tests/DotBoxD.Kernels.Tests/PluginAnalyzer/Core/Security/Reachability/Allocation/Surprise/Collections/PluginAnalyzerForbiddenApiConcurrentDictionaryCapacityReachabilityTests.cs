namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiConcurrentDictionaryCapacityReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_concurrent_dictionary_capacity_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "private static readonly System.Collections.Concurrent.ConcurrentDictionary<byte, byte> Retained = new(concurrencyLevel: 1, capacity: int.MaxValue);"),
            "DotBoxDPluginAnalyzerConcurrentDictionaryCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Concurrent.ConcurrentDictionary", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_default_concurrent_dictionary_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly System.Collections.Concurrent.ConcurrentDictionary<byte, byte> Retained = new();"),
            "DotBoxDPluginAnalyzerDefaultConcurrentDictionaryReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string memberDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("concurrent-dictionary-capacity-static-initializer")]
                public sealed class ConcurrentDictionaryCapacityKernel : IEventKernel<string>
                {
                    {{memberDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

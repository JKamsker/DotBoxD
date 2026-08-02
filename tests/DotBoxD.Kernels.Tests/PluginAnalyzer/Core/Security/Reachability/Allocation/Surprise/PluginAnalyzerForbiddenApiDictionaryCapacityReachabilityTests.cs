namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiDictionaryCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "Dictionary<byte, byte> capacity",
        "private static readonly Dictionary<byte, byte> Retained = new(int.MaxValue);",
        "System.Collections.Generic.Dictionary")]
    [InlineData(
        "Dictionary<byte, byte> bounded capacity",
        "private static readonly Dictionary<byte, byte> Retained = new(4);",
        "System.Collections.Generic.Dictionary")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Retained = System.IO.File.Exists(\"plugin.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_dictionary_capacity_static_initializer(
        string testCase,
        string fieldDeclaration,
        string expectedApi)
    {
        var source = Source(fieldDeclaration);

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            source,
            "DotBoxDPluginAnalyzerDictionaryCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    [Fact]
    public async Task Allows_parameterless_dictionary_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly Dictionary<byte, byte> Retained = new();"),
            "DotBoxDPluginAnalyzerParameterlessDictionaryReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string fieldDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("dictionary-capacity-reachability")]
                public sealed class DictionaryCapacityKernel : IEventKernel<string>
                {
                    {{fieldDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;
                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

}

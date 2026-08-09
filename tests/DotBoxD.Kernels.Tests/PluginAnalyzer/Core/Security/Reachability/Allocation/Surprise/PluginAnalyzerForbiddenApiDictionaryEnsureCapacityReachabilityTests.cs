namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiDictionaryEnsureCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "Dictionary<byte, byte>.EnsureCapacity",
        """
        private static readonly Dictionary<byte, byte> Retained = CreateRetainedDictionary();

        private static Dictionary<byte, byte> CreateRetainedDictionary()
        {
            var retained = new Dictionary<byte, byte>();
            retained.EnsureCapacity(int.MaxValue);
            return retained;
        }
        """,
        "System.Collections.Generic.Dictionary")]
    [InlineData(
        "System.IO positive control",
        "private static readonly bool Retained = System.IO.File.Exists(\"plugin.txt\");",
        "System.IO.File")]
    public async Task Reports_dictionary_ensure_capacity_static_initializer(
        string testCase,
        string memberDeclaration,
        string expectedApi)
    {
        var source = Source(memberDeclaration);

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            source,
            "DotBoxDPluginAnalyzerDictionaryEnsureCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    private static string Source(string memberDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("dictionary-ensure-capacity-reachability")]
                public sealed class DictionaryEnsureCapacityKernel : IEventKernel<string>
                {
                    {{memberDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;
                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

}

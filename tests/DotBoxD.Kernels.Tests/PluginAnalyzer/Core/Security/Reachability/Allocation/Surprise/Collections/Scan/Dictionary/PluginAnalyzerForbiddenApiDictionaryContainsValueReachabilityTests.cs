namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiDictionaryContainsValueReachabilityTests
{
    [Fact]
    public async Task Reports_retained_dictionary_contains_value_scan_in_should_handle()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained[e] = e.ToString();
                return Retained.ContainsValue("missing");
                """),
            "DotBoxDPluginAnalyzerDictionaryContainsValueReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.Dictionary.ContainsValue",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_dictionary_key_lookup_controls()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained[e] = e.ToString();
                _ = Retained.ContainsKey(e);
                _ = Retained.TryGetValue(e, out _);
                return true;
                """),
            "DotBoxDPluginAnalyzerDictionaryContainsValueControlsTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string shouldHandleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("dictionary-contains-value-reachability")]
                public sealed class DictionaryContainsValueKernel : IEventKernel<int>
                {
                    private static readonly Dictionary<int, string> Retained = new();

                    public bool ShouldHandle(int e, HookContext context)
                    {
                        {{shouldHandleBody}}
                    }

                    public void Handle(int e, HookContext context) { }
                }
            }
            """;
}

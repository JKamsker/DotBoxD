namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashSetTrimExcessReachabilityTests
{
    [Fact]
    public async Task Reports_hash_set_trim_excess_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Add(e);
                Retained.TrimExcess();
                """),
            "DotBoxDPluginAnalyzerHashSetTrimExcessReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.HashSet.TrimExcess",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_hash_set_add_and_count()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Add(e);
                _ = Retained.Count;
                """),
            "DotBoxDPluginAnalyzerHashSetAddAndCountReachabilityTest");

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

                [Plugin("hash-set-trim-excess-reachability")]
                public sealed class HashSetTrimExcessKernel : IEventKernel<string>
                {
                    private static readonly HashSet<string> Retained = new();

                    public bool ShouldHandle(string e, HookContext context)
                    {
                        {{shouldHandleBody}}
                        return true;
                    }

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

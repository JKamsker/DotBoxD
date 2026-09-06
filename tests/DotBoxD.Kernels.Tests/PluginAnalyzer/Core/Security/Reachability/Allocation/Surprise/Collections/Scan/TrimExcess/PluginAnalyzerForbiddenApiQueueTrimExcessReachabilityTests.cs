namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiQueueTrimExcessReachabilityTests
{
    [Fact]
    public async Task Reports_queue_trim_excess_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Enqueue(e);
                Retained.TrimExcess();
                """),
            "DotBoxDPluginAnalyzerQueueTrimExcessReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.Queue.TrimExcess",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_queue_enqueue_and_count()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Enqueue(e);
                _ = Retained.Count;
                """),
            "DotBoxDPluginAnalyzerQueueEnqueueAndCountReachabilityTest");

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

                [Plugin("queue-trim-excess-reachability")]
                public sealed class QueueTrimExcessKernel : IEventKernel<string>
                {
                    private static readonly Queue<string> Retained = new();

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

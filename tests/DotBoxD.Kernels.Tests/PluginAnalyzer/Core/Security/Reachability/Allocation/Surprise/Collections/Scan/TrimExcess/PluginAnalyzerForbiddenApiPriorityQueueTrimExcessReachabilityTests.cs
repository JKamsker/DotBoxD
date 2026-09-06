namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiPriorityQueueTrimExcessReachabilityTests
{
    [Fact]
    public async Task Reports_priority_queue_trim_excess_in_reachable_event_handler()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Enqueue(e, 0);
                Retained.TrimExcess();
                """),
            "DotBoxDPluginAnalyzerPriorityQueueTrimExcessReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.PriorityQueue.TrimExcess",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_bounded_priority_queue_enqueue_and_count()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                """
                Retained.Enqueue(e, 0);
                _ = Retained.Count;
                """),
            "DotBoxDPluginAnalyzerPriorityQueueEnqueueAndCountReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string handleBody)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("priority-queue-trim-excess-reachability")]
                public sealed class PriorityQueueTrimExcessKernel : IEventKernel<string>
                {
                    private static readonly PriorityQueue<string, int> Retained = new();

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context)
                    {
                        {{handleBody}}
                    }
                }
            }
            """;
}

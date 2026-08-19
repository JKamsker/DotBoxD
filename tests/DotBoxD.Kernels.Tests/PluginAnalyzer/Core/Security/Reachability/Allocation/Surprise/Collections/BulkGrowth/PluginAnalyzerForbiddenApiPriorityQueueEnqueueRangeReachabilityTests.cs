namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiPriorityQueueEnqueueRangeReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_priority_queue_enqueue_range_static_initializer()
    {
        const string memberDeclarations = """
            private static readonly PriorityQueue<byte, byte> Retained = CreateRetainedQueue();

            private static PriorityQueue<byte, byte> CreateRetainedQueue()
            {
                var queue = new PriorityQueue<byte, byte>();
                queue.EnqueueRange(Enumerable.Repeat((byte)0, int.MaxValue), (byte)0);
                return queue;
            }
            """;

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(memberDeclarations),
            "DotBoxDPluginAnalyzerPriorityQueueEnqueueRangeReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Generic.PriorityQueue.EnqueueRange",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    private static string Source(string memberDeclarations)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("priority-queue-enqueue-range-reachability")]
                public sealed class PriorityQueueEnqueueRangeKernel : IEventKernel<string>
                {
                    {{memberDeclarations}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

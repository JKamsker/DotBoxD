namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiPriorityQueueEnsureCapacityReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_priority_queue_ensure_capacity_static_initializer()
    {
        const string memberDeclarations = """
            private static readonly PriorityQueue<byte, byte> Retained = CreateRetainedQueue();

            private static PriorityQueue<byte, byte> CreateRetainedQueue()
            {
                var retained = new PriorityQueue<byte, byte>();
                retained.EnsureCapacity(int.MaxValue);
                return retained;
            }
            """;

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(memberDeclarations),
            "DotBoxDPluginAnalyzerPriorityQueueEnsureCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.Contains("System.Collections.Generic.PriorityQueue", message, StringComparison.Ordinal);
    }

    private static string Source(string memberDeclarations)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("priority-queue-ensure-capacity-reachability")]
                public sealed class PriorityQueueEnsureCapacityKernel : IEventKernel<string>
                {
                    {{memberDeclarations}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

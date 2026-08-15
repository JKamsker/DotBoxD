namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiQueueEnsureCapacityReachabilityTests
{
    [Fact]
    public async Task Reports_queue_ensure_capacity_static_initializer()
    {
        var source = Source();

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            source,
            "DotBoxDPluginAnalyzerQueueEnsureCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.Queue", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source()
        => """
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("queue-ensure-capacity-reachability")]
                public sealed class QueueEnsureCapacityKernel : IEventKernel<string>
                {
                    private static readonly Queue<byte> Retained = CreateRetainedQueue();

                    private static Queue<byte> CreateRetainedQueue()
                    {
                        var retained = new Queue<byte>();
                        retained.EnsureCapacity(int.MaxValue);
                        return retained;
                    }

                    public bool ShouldHandle(string e, HookContext context) => true;
                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

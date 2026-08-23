namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiQueueEnumerableConstructionReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_queue_enumerable_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new Queue<byte>(Enumerable.Repeat((byte)0, int.MaxValue))"),
            "DotBoxDPluginAnalyzerQueueEnumerableConstructionReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.Generic.Queue", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_report_empty_queue_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("new Queue<byte>()"),
            "DotBoxDPluginAnalyzerEmptyQueueConstructorReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string initializer)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("queue-enumerable-construction-reachability")]
                public sealed class QueueEnumerableConstructionKernel : IEventKernel<string>
                {
                    private static readonly Queue<byte> Retained = {{initializer}};

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

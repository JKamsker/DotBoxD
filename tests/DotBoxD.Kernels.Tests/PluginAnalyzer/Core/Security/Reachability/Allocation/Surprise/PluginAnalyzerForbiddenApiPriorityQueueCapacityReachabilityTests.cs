namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiPriorityQueueCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "PriorityQueue<byte, byte> capacity",
        "private static readonly PriorityQueue<byte, byte> Retained = new(int.MaxValue);",
        "System.Collections.Generic.PriorityQueue")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Retained = System.IO.File.Exists(\"/x\");",
        "System.IO.File")]
    public async Task Reports_unbounded_priority_queue_capacity_static_initializer(
        string testCase,
        string fieldDeclaration,
        string expectedApi)
    {
        var source = Source(fieldDeclaration);

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            source,
            "DotBoxDPluginAnalyzerPriorityQueueCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains(expectedApi, StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    private static string Source(string fieldDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("priority-queue-capacity")]
                public sealed class PriorityQueueKernel : IEventKernel<string>
                {
                    {{fieldDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

}

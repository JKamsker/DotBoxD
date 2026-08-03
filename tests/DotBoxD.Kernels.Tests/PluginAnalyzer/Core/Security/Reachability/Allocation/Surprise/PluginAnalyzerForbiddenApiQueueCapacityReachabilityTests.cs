namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiQueueCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "Queue<byte> capacity",
        "private static readonly System.Collections.Generic.Queue<byte> Retained = new(int.MaxValue);",
        "System.Collections.Generic.Queue")]
    [InlineData(
        "Queue<byte> bounded capacity",
        "private static readonly System.Collections.Generic.Queue<byte> Retained = new(4);",
        "System.Collections.Generic.Queue")]
    [InlineData(
        "System.IO positive control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_queue_capacity_static_initializer(
        string testCase,
        string memberDeclaration,
        string expectedApi)
    {
        var source = Source(memberDeclaration);

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            source,
            "DotBoxDPluginAnalyzerQueueCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    private static string Source(string memberDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("queue-capacity-static-initializer")]
                public sealed class QueueCapacityKernel : IEventKernel<string>
                {
                    {{memberDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

}

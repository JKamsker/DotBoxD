namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiNonGenericQueueCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "Queue capacity",
        "private static readonly System.Collections.Queue Retained = new(int.MaxValue);",
        "System.Collections.Queue",
        true)]
    [InlineData(
        "parameterless Queue control",
        "private static readonly System.Collections.Queue Retained = new();",
        "",
        false)]
    [InlineData(
        "System.IO positive control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin.txt\");",
        "System.IO.File",
        true)]
    public async Task Reports_unbounded_non_generic_queue_capacity_static_initializer(
        string testCase,
        string memberDeclaration,
        string expectedApi,
        bool expectsForbiddenApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(memberDeclaration),
            "DotBoxDPluginAnalyzerNonGenericQueueCapacityReachabilityTest");

        var forbiddenApiDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001");

        if (!expectsForbiddenApi)
        {
            Assert.Empty(forbiddenApiDiagnostics);
            return;
        }

        var diagnostic = Assert.Single(forbiddenApiDiagnostics);
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

                [Plugin("non-generic-queue-capacity-static-initializer")]
                public sealed class NonGenericQueueCapacityKernel : IEventKernel<string>
                {
                    {{memberDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

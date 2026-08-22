namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiNonGenericQueueCollectionConstructorReachabilityTests
{
    [Fact]
    public async Task Reports_unbounded_queue_collection_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(
                "private static readonly System.Collections.ICollection Source = new System.Collections.ArrayList();",
                "private static readonly System.Collections.Queue Retained = new(Source);"),
            "DotBoxDPluginAnalyzerNonGenericQueueCollectionConstructorReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(item => item.Id == "DBXK001"));

        Assert.Contains("System.Collections.Queue", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_parameterless_queue_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(null, "private static readonly System.Collections.Queue Retained = new();"),
            "DotBoxDPluginAnalyzerParameterlessNonGenericQueueReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_queue_capacity_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(null, "private static readonly System.Collections.Queue Retained = new(int.MaxValue);"),
            "DotBoxDPluginAnalyzerNonGenericQueueCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(item => item.Id == "DBXK001"));

        Assert.Contains("System.Collections.Queue", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source(string? sourceDeclaration, string retainedDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("non-generic-queue-collection-constructor-reachability")]
                public sealed class NonGenericQueueCollectionConstructorKernel : IEventKernel<string>
                {
                    {{sourceDeclaration}}
                    {{retainedDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

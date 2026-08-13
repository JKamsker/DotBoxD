namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiCollectionBaseCapacityReachabilityTests
{
    [Fact]
    public async Task Reports_derived_collection_base_capacity_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly RetainedCollection Retained = new(int.MaxValue);"),
            "DotBoxDPluginAnalyzerCollectionBaseCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.CollectionBase",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_parameterless_derived_collection_base_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly RetainedCollection Retained = new();"),
            "DotBoxDPluginAnalyzerParameterlessCollectionBaseReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string fieldDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("collection-base-capacity-reachability")]
                public sealed class CollectionBaseCapacityKernel : IEventKernel<string>
                {
                    {{fieldDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }

                    private sealed class RetainedCollection : System.Collections.CollectionBase
                    {
                        public RetainedCollection() { }

                        public RetainedCollection(int capacity)
                            : base(capacity) { }
                    }
                }
            }
            """;
}

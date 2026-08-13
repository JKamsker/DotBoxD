namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiNameObjectCollectionBaseCapacityReachabilityTests
{
    [Fact]
    public async Task Reports_derived_name_object_collection_base_capacity_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly RetainedCollection Retained = new(int.MaxValue);"),
            "DotBoxDPluginAnalyzerNameObjectCollectionBaseCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains(
            "System.Collections.Specialized.NameObjectCollectionBase",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_parameterless_derived_name_object_collection_base_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly RetainedCollection Retained = new();"),
            "DotBoxDPluginAnalyzerParameterlessNameObjectCollectionBaseReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    private static string Source(string fieldDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("name-object-collection-base-capacity-reachability")]
                public sealed class NameObjectCollectionBaseCapacityKernel : IEventKernel<string>
                {
                    {{fieldDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }

                    private sealed class RetainedCollection : System.Collections.Specialized.NameObjectCollectionBase
                    {
                        public RetainedCollection() { }

                        public RetainedCollection(int capacity)
                            : base(capacity) { }
                    }
                }
            }
            """;
}

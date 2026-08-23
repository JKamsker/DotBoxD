namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiArrayListCollectionConstructorReachabilityTests
{
    [Fact]
    public async Task Reports_array_list_collection_constructor_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("""
                private static readonly System.Collections.ICollection Source = System.Array.Empty<object>();
                private static readonly System.Collections.ArrayList Retained = new(Source);
                """),
            "DotBoxDPluginAnalyzerArrayListCollectionConstructorReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.ArrayList", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allows_parameterless_array_list_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly System.Collections.ArrayList Retained = new();"),
            "DotBoxDPluginAnalyzerParameterlessArrayListCollectionConstructorReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_array_list_capacity_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly System.Collections.ArrayList Retained = new(int.MaxValue);"),
            "DotBoxDPluginAnalyzerArrayListCapacityCollectionConstructorReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        Assert.Contains("System.Collections.ArrayList", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source(string memberDeclarations)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("array-list-collection-constructor-static-initializer")]
                public sealed class ArrayListCollectionConstructorKernel : IEventKernel<string>
                {
                    {{memberDeclarations}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

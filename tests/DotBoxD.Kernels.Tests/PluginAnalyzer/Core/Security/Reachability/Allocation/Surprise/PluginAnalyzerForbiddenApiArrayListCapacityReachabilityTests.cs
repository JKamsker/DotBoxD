namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiArrayListCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "ArrayList capacity",
        "private static readonly System.Collections.ArrayList Retained = new(int.MaxValue);",
        "System.Collections.ArrayList")]
    [InlineData(
        "System.IO positive control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin.txt\");",
        "System.IO.File")]
    public async Task Reports_array_list_capacity_static_initializer(
        string testCase,
        string memberDeclaration,
        string expectedApi)
    {
        var source = Source(memberDeclaration);

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            source,
            "DotBoxDPluginAnalyzerArrayListCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    [Fact]
    public async Task Allows_parameterless_array_list_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly System.Collections.ArrayList Retained = new();"),
            "DotBoxDPluginAnalyzerParameterlessArrayListReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Reports_array_list_capacity_setter_reachable_from_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("""
                private static readonly int Retained = Reserve();

                private static int Reserve()
                {
                    var list = new System.Collections.ArrayList();
                    list.Capacity = int.MaxValue;
                    return list.Count;
                }
                """),
            "DotBoxDPluginAnalyzerArrayListCapacitySetterReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        Assert.Contains("System.Collections.ArrayList", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private static string Source(string memberDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("array-list-capacity-static-initializer")]
                public sealed class ArrayListCapacityKernel : IEventKernel<string>
                {
                    {{memberDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

}

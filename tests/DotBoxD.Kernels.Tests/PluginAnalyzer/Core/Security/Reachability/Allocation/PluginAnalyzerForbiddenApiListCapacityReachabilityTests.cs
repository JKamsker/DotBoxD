namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiListCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "List<byte> capacity",
        "private static readonly List<byte> Retained = new(int.MaxValue);",
        "System.Collections.Generic.List")]
    [InlineData(
        "List<byte> bounded capacity",
        "private static readonly List<byte> Retained = new(4);",
        "System.Collections.Generic.List")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Retained = System.IO.File.Exists(\"plugin.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_list_capacity_static_initializer(
        string testCase,
        string fieldDeclaration,
        string expectedApi)
    {
        var source = Source(fieldDeclaration);

        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            source,
            "DotBoxDPluginAnalyzerListCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    [Fact]
    public async Task Allows_parameterless_list_static_initializer()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source("private static readonly List<byte> Retained = new();"),
            "DotBoxDPluginAnalyzerParameterlessListReachabilityTest");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK001");
    }

    [Fact]
    public async Task Capacity_constructor_keeps_forbidden_generic_argument_reachability()
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            """
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-capacity-generic-reachability")]
                public sealed class ListCapacityKernel : IEventKernel<string>
                {
                    public bool ShouldHandle(string e, HookContext context) => Helper();

                    public void Handle(string e, HookContext context) { }

                    private static bool Helper()
                    {
                        _ = new List<System.IO.FileInfo>(4);
                        return true;
                    }
                }
            }
            """,
            "DotBoxDPluginAnalyzerListCapacityGenericReachabilityTest");

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK001" &&
                          diagnostic.GetMessage().Contains("System.IO.FileInfo", StringComparison.Ordinal));
    }

    private static string Source(string fieldDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Generic;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("list-capacity-reachability")]
                public sealed class ListCapacityKernel : IEventKernel<string>
                {
                    {{fieldDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;
                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

}

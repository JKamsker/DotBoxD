namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiEnumerableToArrayReachabilityTests
{
    [Theory]
    [InlineData(
        "Enumerable.Repeat ToArray materialization",
        "private static readonly byte[] Retained = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Repeat((byte)0, int.MaxValue));",
        "System.Linq.Enumerable.ToArray")]
    [InlineData(
        "System.IO positive control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_enumerable_materialization_static_initializer(
        string testCase,
        string memberDeclaration,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(memberDeclaration),
            "DotBoxDPluginAnalyzerEnumerableToArrayReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
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

                [Plugin("enumerable-to-array-static-initializer")]
                public sealed class EnumerableToArrayKernel : IEventKernel<string>
                {
                    {{memberDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

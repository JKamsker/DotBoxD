namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiImmutableHashSetToImmutableHashSetReachabilityTests
{
    [Theory]
    [InlineData(
        "ImmutableHashSet materialization",
        "private static readonly ImmutableHashSet<int> Retained = Enumerable.Range(0, int.MaxValue).ToImmutableHashSet();",
        "System.Collections.Immutable.ImmutableHashSet.ToImmutableHashSet")]
    [InlineData(
        "System.IO positive control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_immutable_hash_set_materialization_static_initializer(
        string testCase,
        string memberDeclaration,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(memberDeclaration),
            "DotBoxDPluginAnalyzerImmutableHashSetToImmutableHashSetReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(message.Contains(expectedApi, StringComparison.Ordinal), $"{testCase}: {message}");
    }

    private static string Source(string memberDeclaration)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using System.Collections.Immutable;
                using System.Linq;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("immutable-hash-set-static-initializer")]
                public sealed class ImmutableHashSetKernel : IEventKernel<string>
                {
                    {{memberDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

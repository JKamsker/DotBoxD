namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiHashSetCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "HashSet<byte> capacity",
        "private static readonly System.Collections.Generic.HashSet<byte> Retained = new(int.MaxValue);",
        "System.Collections.Generic.HashSet")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin-hashset.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_hash_set_capacity_static_initializer(
        string testCase,
        string staticMember,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(staticMember),
            "DotBoxDPluginAnalyzerHashSetCapacityReachabilityTest");

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == "DBXK001"));
        var message = diagnostic.GetMessage();
        Assert.True(
            message.Contains(expectedApi, StringComparison.Ordinal),
            $"{testCase}: {message}");
    }

    private static string Source(string staticMember)
        => $$"""
            #nullable enable

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("hashset-capacity-reachability")]
                public sealed class HashSetCapacityKernel : IEventKernel<string>
                {
                    {{staticMember}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

}

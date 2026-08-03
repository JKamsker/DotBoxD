namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiImmutableArrayBuilderCapacityReachabilityTests
{
    [Theory]
    [InlineData(
        "ImmutableArray<byte>.Builder capacity",
        "private static readonly ImmutableArray<byte>.Builder Retained = ImmutableArray.CreateBuilder<byte>(int.MaxValue);",
        "System.Collections.Immutable.ImmutableArray")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin-immutable-array.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_immutable_array_builder_capacity_static_initializer(
        string testCase,
        string staticMember,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(staticMember),
            "DotBoxDPluginAnalyzerImmutableArrayBuilderCapacityReachabilityTest");

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
                using System.Collections.Immutable;
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                [Plugin("immutable-array-builder-capacity")]
                public sealed class ImmutableArrayBuilderCapacityKernel : IEventKernel<string>
                {
                    {{staticMember}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;

}

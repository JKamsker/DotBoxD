namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerForbiddenApiImmutableArrayBuilderAddRangeReachabilityTests
{
    [Theory]
    [InlineData(
        "ImmutableArray<byte>.Builder AddRange",
        "private static readonly ImmutableArray<byte>.Builder Retained = CreateRetainedBuilder();\n\n    private static ImmutableArray<byte>.Builder CreateRetainedBuilder()\n    {\n        var builder = ImmutableArray.CreateBuilder<byte>();\n        builder.AddRange(Enumerable.Repeat((byte)0, int.MaxValue));\n        return builder;\n    }",
        "System.Collections.Immutable.ImmutableArray<byte>.Builder.AddRange")]
    [InlineData(
        "direct System.IO control",
        "private static readonly bool Exists = System.IO.File.Exists(\"plugin-immutable-array-builder.txt\");",
        "System.IO.File")]
    public async Task Reports_unbounded_immutable_array_builder_growth_static_initializer(
        string testCase,
        string memberDeclaration,
        string expectedApi)
    {
        var diagnostics = await PluginAnalyzerCapacityTestHarness.AnalyzeAsync(
            Source(memberDeclaration),
            "DotBoxDPluginAnalyzerImmutableArrayBuilderAddRangeReachabilityTest");

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

                [Plugin("immutable-array-builder-addrange")]
                public sealed class ImmutableArrayBuilderAddRangeKernel : IEventKernel<string>
                {
                    {{memberDeclaration}}

                    public bool ShouldHandle(string e, HookContext context) => true;

                    public void Handle(string e, HookContext context) { }
                }
            }
            """;
}

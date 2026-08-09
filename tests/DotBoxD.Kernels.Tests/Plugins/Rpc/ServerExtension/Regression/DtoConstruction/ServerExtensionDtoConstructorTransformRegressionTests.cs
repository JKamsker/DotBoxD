using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDtoConstructorTransformRegressionTests
{
    [Fact]
    public void Server_extension_rejects_dto_constructor_that_transforms_matching_member()
        => AssertConstructorRejected("""
                public Score(int value)
                {
                    Value = value + 1;
                }
            """);

    [Fact]
    public void Server_extension_rejects_dto_constructor_that_omits_matching_member_assignment()
        => AssertConstructorRejected("""
                public Score(int value)
                {
                }
            """);

    [Fact]
    public void Server_extension_rejects_dto_constructor_that_assigns_matching_member_conditionally()
        => AssertConstructorRejected("""
                public Score(int value)
                {
                    if (value >= 0)
                    {
                        Value = value;
                    }
                }
            """);

    [Fact]
    public void Server_extension_rejects_dto_constructor_that_delegates_matching_member_assignment()
        => AssertConstructorRejected("""
                public Score()
                {
                    Value = 0;
                }

                public Score(int value)
                    : this()
                {
                }
            """);

    private static void AssertConstructorRejected(string constructor)
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator($$"""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class Score
            {
            {{constructor}}

                public int Value { get; }
            }

            [ServerExtension("score-transform")]
            public sealed partial class ScoreKernel
            {
                public Score Read(int value, HookContext ctx)
                {
                    return new Score(value);
                }
            }
            """);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                diagnostic.GetMessage().Contains("Score", StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains("constructor", StringComparison.Ordinal));
        Assert.Empty(result.GeneratedTrees);
    }
}

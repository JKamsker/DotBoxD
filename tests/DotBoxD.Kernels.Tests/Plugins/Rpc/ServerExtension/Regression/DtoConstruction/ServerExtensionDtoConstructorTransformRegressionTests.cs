using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDtoConstructorTransformRegressionTests
{
    [Fact]
    public void Server_extension_rejects_dto_constructor_that_transforms_matching_member()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class Score
            {
                public Score(int value)
                {
                    Value = value + 1;
                }

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

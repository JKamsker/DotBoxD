using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.TestFixtures.MergeableIr;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class LoweredPipelineComposerIdentifierValidationTests
{
    [Fact]
    public void Null_composition_fails_at_composer_boundary()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            LoweredPipelineComposer.Compose(null!));

        Assert.Equal("composition", exception.ParamName);
    }

    [Theory]
    [InlineData(nameof(LoweredPipelineComposition.ModuleId))]
    [InlineData(nameof(LoweredPipelineComposition.ShouldHandleFunctionId))]
    [InlineData(nameof(LoweredPipelineComposition.HandleFunctionId))]
    public void Whitespace_composition_ids_fail_at_composer_boundary(string memberName)
    {
        var composition = WithWhitespaceId(memberName);

        var exception = Assert.Throws<ArgumentException>(() =>
            LoweredPipelineComposer.Compose(composition));

        Assert.True(
            string.Equals(exception.ParamName, memberName, StringComparison.Ordinal) ||
            exception.Message.Contains(memberName, StringComparison.Ordinal),
            $"Expected ParamName or message to name {memberName}, but got ParamName '{exception.ParamName}' and message '{exception.Message}'.");
    }

    [Fact]
    public void Duplicate_entrypoint_ids_fail_at_composer_boundary()
    {
        var composition = new LoweredPipelineComposition(
            "mergeable-pipeline",
            MergeableIrPipelineFixture.ConfigureSteps(),
            SandboxType.String)
        {
            ShouldHandleFunctionId = "Handle",
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            LoweredPipelineComposer.Compose(composition));

        Assert.Equal("composition", exception.ParamName);
        Assert.Contains("distinct", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_steps_fail_at_composer_boundary()
    {
        var composition = new LoweredPipelineComposition(
            "mergeable-pipeline",
            [],
            SandboxType.String);

        var exception = Assert.Throws<ArgumentException>(() =>
            LoweredPipelineComposer.Compose(composition));

        Assert.Equal("composition", exception.ParamName);
        Assert.Contains("at least one step", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_composition_ids_still_compose()
    {
        var module = LoweredPipelineComposer.Compose(new LoweredPipelineComposition(
            "mergeable-pipeline",
            MergeableIrPipelineFixture.ConfigureSteps(),
            SandboxType.String));

        Assert.Equal("mergeable-pipeline", module.Id);
        Assert.Contains(module.Functions, function => function.Id == "ShouldHandle");
        Assert.Contains(module.Functions, function => function.Id == "Handle");
    }

    private static LoweredPipelineComposition WithWhitespaceId(string memberName)
    {
        var composition = new LoweredPipelineComposition(
            "mergeable-pipeline",
            MergeableIrPipelineFixture.ConfigureSteps(),
            SandboxType.String);

        return memberName switch
        {
            nameof(LoweredPipelineComposition.ModuleId) => composition with { ModuleId = "   " },
            nameof(LoweredPipelineComposition.ShouldHandleFunctionId) => composition with
            {
                ShouldHandleFunctionId = "   ",
            },
            nameof(LoweredPipelineComposition.HandleFunctionId) => composition with
            {
                HandleFunctionId = "   ",
            },
            _ => throw new ArgumentOutOfRangeException(nameof(memberName), memberName, null),
        };
    }
}

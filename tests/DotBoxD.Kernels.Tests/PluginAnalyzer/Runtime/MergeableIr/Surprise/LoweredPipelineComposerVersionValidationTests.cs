using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.TestFixtures.MergeableIr;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class LoweredPipelineComposerVersionValidationTests
{
    [Theory]
    [InlineData(nameof(LoweredPipelineComposition.Version))]
    [InlineData(nameof(LoweredPipelineComposition.TargetSandboxVersion))]
    public void Null_composition_versions_fail_at_composer_boundary(string memberName)
    {
        var composition = ValidComposition() with
        {
            Version = memberName == nameof(LoweredPipelineComposition.Version) ? null! : new(1, 0, 0),
            TargetSandboxVersion = memberName == nameof(LoweredPipelineComposition.TargetSandboxVersion)
                ? null!
                : new(1, 0, 0),
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            LoweredPipelineComposer.Compose(composition));

        Assert.True(
            string.Equals(exception.ParamName, memberName, StringComparison.Ordinal) ||
            exception.Message.Contains(memberName, StringComparison.Ordinal),
            $"Expected ParamName or message to name {memberName}, but got ParamName '{exception.ParamName}' and message '{exception.Message}'.");
    }

    [Fact]
    public void Valid_composition_versions_still_compose()
    {
        var module = LoweredPipelineComposer.Compose(ValidComposition());

        Assert.Equal(new SemVersion(1, 0, 0), module.Version);
        Assert.Equal(new SemVersion(1, 0, 0), module.TargetSandboxVersion);
    }

    private static LoweredPipelineComposition ValidComposition()
        => new(
            "mergeable-pipeline",
            MergeableIrPipelineFixture.ConfigureSteps(),
            SandboxType.String);
}

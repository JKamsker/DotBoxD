using DotBoxD.Kernels.TestFixtures.MergeableIr;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class MergeableIrFixtureRuntimeTests
{
    [Fact]
    public void Build_time_source_generator_lowers_custom_pipeline_steps_for_runtime_execution()
    {
        var matched = MergeableIrPipelineFixture.Project(new ProbeEvent(5, "target-1"));
        var filtered = MergeableIrPipelineFixture.Project(new ProbeEvent(3, "target-2"));
        var steps = MergeableIrPipelineFixture.ConfigureSteps();

        Assert.Equal("target-1", matched);
        Assert.Null(filtered);
        Assert.Collection(
            steps,
            step =>
            {
                Assert.Equal(LoweredPipelineStepKind.Filter, step.Kind);
                Assert.Equal("record", step.InputType);
                Assert.Equal("bool", step.OutputType);
                Assert.Contains("probe.read.distance", step.RequiredCapabilities);
            },
            step =>
            {
                Assert.Equal(LoweredPipelineStepKind.Projection, step.Kind);
                Assert.Equal("record", step.InputType);
                Assert.Equal("string", step.OutputType);
            });
    }

    [Fact]
    public void Fixture_Where_rejects_a_runtime_null_ir_body()
    {
        var pipeline = new StepPipeline<ProbeEvent>();
        IRFunc<ProbeEvent, bool>? irPredicate = null;

        var exception = Assert.Throws<ArgumentNullException>(
            () => pipeline.Where(e => e.Distance >= 4, irPredicate));

        Assert.Equal(nameof(irPredicate), exception.ParamName);
    }

    [Fact]
    public void Fixture_Select_rejects_a_runtime_null_ir_body()
    {
        var pipeline = new StepPipeline<ProbeEvent>();
        IRFunc<ProbeEvent, string>? irSelector = null;

        var exception = Assert.Throws<ArgumentNullException>(
            () => pipeline.Select(e => e.TargetId, irSelector));

        Assert.Equal(nameof(irSelector), exception.ParamName);
    }
}

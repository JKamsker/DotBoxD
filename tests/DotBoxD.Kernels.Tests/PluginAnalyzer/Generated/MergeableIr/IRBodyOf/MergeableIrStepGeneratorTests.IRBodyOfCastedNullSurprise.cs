namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class MergeableIrStepGeneratorTests
{
    [Fact]
    public void Generator_treats_casted_null_ir_body_argument_as_default_companion()
    {
        var result = RunGeneratorAndAssertCompiles("""
            using System;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;

            namespace Sample;

            public sealed record ProbeEvent([property: Capability("probe.read.distance")] int Distance);

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
                    => this;
            }

            public static class Usage
            {
                public static StepPipeline<ProbeEvent> Configure(StepPipeline<ProbeEvent> pipeline)
                    => pipeline.Where(
                        predicate: e => e.Distance >= 4,
                        irPredicate: (IRFunc<ProbeEvent, bool>?)null);
            }
            """);

        var generated = GeneratedSource(result);

        Assert.Contains("LoweredPipelineStep_", GeneratedHintNames(result), StringComparison.Ordinal);
        Assert.Contains("IRFunc<global::Sample.ProbeEvent, bool>? @irPredicate = null", generated, StringComparison.Ordinal);
        Assert.Contains("@irPredicate:", generated, StringComparison.Ordinal);
        Assert.Contains("LoweredPipelineStepKind.Filter", generated, StringComparison.Ordinal);
    }
}

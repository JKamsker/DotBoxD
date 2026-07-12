using System.Globalization;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class MergeableIrStepGeneratorTests
{
    [Fact]
    public void Marked_extension_receiver_call_reports_DBXK100_without_lowered_output()
    {
        var result = RunGenerator("""
            using System;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record ProbeEvent(int Distance);

            public sealed class StepPipeline<T>;

            public static class PipelineExtensions
            {
                public static StepPipeline<T> Where<T>(
                    this StepPipeline<T> pipeline,
                    [LowerToIr(LoweredPipelineStepKind.Filter)] Func<T, bool> predicate)
                    => throw new InvalidOperationException("not lowered");

                public static StepPipeline<T> Where<T>(
                    this StepPipeline<T> pipeline,
                    LoweredPipelineStep step)
                    => pipeline;
            }

            public static class Usage
            {
                public static StepPipeline<ProbeEvent> Configure(StepPipeline<ProbeEvent> pipeline)
                    => pipeline.Where(e => e.Distance > 0);
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Id == "DBXK100");
        Assert.Contains("extension", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.FilePath.Contains("LoweredPipelineStep_", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.FilePath.Contains("DotBoxDMergeableIrStepInterceptors", StringComparison.Ordinal));
    }
}

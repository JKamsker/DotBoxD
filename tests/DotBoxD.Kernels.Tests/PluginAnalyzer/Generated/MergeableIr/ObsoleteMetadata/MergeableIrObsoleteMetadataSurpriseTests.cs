using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class MergeableIrStepGeneratorTests
{
    [Fact]
    public void Generator_rejects_or_preserves_error_obsolete_ir_body_payload_contract()
    {
        var result = RunGenerator(
            """
            using System;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;

            namespace Sample;

            [Obsolete("Legacy events are no longer supported", error: true)]
            public sealed record LegacyEvent(int Distance);

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
                    => this;
            }

            public static class Usage
            {
                [Obsolete("Legacy pipeline configuration", error: true)]
                public static StepPipeline<LegacyEvent> Configure(StepPipeline<LegacyEvent> pipeline)
                    => pipeline.Where(@event => @event.Distance >= 4);
            }
            """,
            out var outputCompilation,
            out var generatorDiagnostics);

        var allDiagnostics = result.Diagnostics
            .Concat(generatorDiagnostics)
            .Concat(outputCompilation.GetDiagnostics())
            .ToArray();
        var focusedDiagnostics = allDiagnostics
            .Where(diagnostic => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal) &&
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.GetMessage().Contains("obsolete", StringComparison.OrdinalIgnoreCase) &&
                diagnostic.GetMessage().Contains("LegacyEvent", StringComparison.Ordinal))
            .ToArray();
        var generatedObsoleteDiagnostics = allDiagnostics
            .Where(diagnostic => diagnostic.Id is "CS0618" or "CS0619")
            .Where(diagnostic => diagnostic.Location.GetLineSpan().Path.Contains(
                "LoweredPipelineStep_",
                StringComparison.Ordinal) ||
                diagnostic.Location.GetLineSpan().Path.Contains(
                    "DotBoxDMergeableIrStepInterceptors.g.cs",
                    StringComparison.Ordinal))
            .ToArray();

        if (focusedDiagnostics.Length > 0)
        {
            Assert.Empty(generatedObsoleteDiagnostics);
            return;
        }

        var generated = GeneratedSource(result);
        Assert.Empty(generatedObsoleteDiagnostics);
        Assert.Contains(
            "[global::System.ObsoleteAttribute(\"Legacy events are no longer supported\", true)]",
            generated,
            StringComparison.Ordinal);
    }
}

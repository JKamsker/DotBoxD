using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class MergeableIrStepGeneratorTests
{
    [Fact]
    public void Generator_preserves_or_rejects_preview_feature_ir_body_payload_contract()
    {
        var result = RunGenerator(
            """
            using System;
            using System.Runtime.Versioning;
            using DotBoxD.Abstractions;

            namespace Sample;

            [RequiresPreviewFeatures("Preview payload contract", Url = "https://example.invalid/preview-event")]
            public sealed record PreviewEvent(int Value);

            public sealed record PortableEvent(int Value);

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
                    => this;
            }

            public static class Usage
            {
                [RequiresPreviewFeatures("Preview pipeline configuration")]
                public static StepPipeline<PreviewEvent> ConfigurePreview(StepPipeline<PreviewEvent> pipeline)
                    => pipeline.Where(item => item.Value >= 4);

                public static StepPipeline<PortableEvent> ConfigurePortable(StepPipeline<PortableEvent> pipeline)
                    => pipeline.Where(item => item.Value >= 4);
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
                diagnostic.GetMessage().Contains("preview", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (focusedDiagnostics.Length > 0)
        {
            return;
        }

        var generated = GeneratedSource(result);
        const string previewAttribute =
            "[global::System.Runtime.Versioning.RequiresPreviewFeaturesAttribute(\"Preview payload contract\", Url = \"https://example.invalid/preview-event\")]";

        Assert.Contains(
            previewAttribute +
            "\n    public static global::DotBoxD.Abstractions.IRFunc<global::Sample.PreviewEvent, bool> CreateIRFunc()",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            previewAttribute +
            "\n        public static global::Sample.StepPipeline<global::Sample.PreviewEvent> Intercept_",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            previewAttribute +
            "\n    public static global::DotBoxD.Abstractions.IRFunc<global::Sample.PortableEvent, bool> CreateIRFunc()",
            generated,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            previewAttribute +
            "\n        public static global::Sample.StepPipeline<global::Sample.PortableEvent> Intercept_",
            generated,
            StringComparison.Ordinal);
    }
}

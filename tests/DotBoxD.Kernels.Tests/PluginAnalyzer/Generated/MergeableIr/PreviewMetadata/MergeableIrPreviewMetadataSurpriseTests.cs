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

    [Fact]
    public void Generator_preserves_parameterless_preview_feature_ir_body_payload_contract()
    {
        var result = RunGenerator(
            """
            using System;
            using System.Runtime.Versioning;
            using DotBoxD.Abstractions;

            namespace Sample;

            [RequiresPreviewFeatures]
            public sealed record PreviewEvent(int Value);

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
                    => this;
            }

            public static class Usage
            {
                public static StepPipeline<PreviewEvent> Configure(StepPipeline<PreviewEvent> pipeline)
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

        Assert.Empty(focusedDiagnostics);
        Assert.Contains(
            "[global::System.Runtime.Versioning.RequiresPreviewFeaturesAttribute]\n" +
            "    public static global::DotBoxD.Abstractions.IRFunc<global::Sample.PreviewEvent, bool> CreateIRFunc()",
            GeneratedSource(result),
            StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("(Url = \"https://example.test/preview\")", 0)]
    [InlineData("(null, Url = \"https://example.test/preview\")", 1)]
    public void Generated_ir_members_preserve_preview_constructor_shapes_and_url(string arguments, int constructorCount)
    {
        RunGenerator($$"""
            using System;
            using System.Runtime.Versioning;
            using DotBoxD.Abstractions;
            namespace Sample;
            [RequiresPreviewFeatures{{arguments}}]
            public sealed record PreviewEvent(int Value);
            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null) => this;
            }
            public static class Usage
            {
                public static StepPipeline<PreviewEvent> Configure(StepPipeline<PreviewEvent> pipeline)
                    => pipeline.Where(item => item.Value >= 4);
            }
            """, out var output, out var diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var methods = output.GetSymbolsWithName(name => name == "CreateIRFunc" || name.StartsWith("Intercept_", StringComparison.Ordinal))
            .OfType<IMethodSymbol>().ToArray();
        Assert.Equal(2, methods.Length);
        foreach (var method in methods)
        {
            var attribute = Assert.Single(method.GetAttributes(), a =>
                a.AttributeClass?.ToDisplayString() == "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute");
            Assert.Equal(constructorCount, attribute.ConstructorArguments.Length);
            Assert.Equal("https://example.test/preview", Assert.Single(attribute.NamedArguments).Value.Value);
        }
    }

    [Fact]
    public void Aliased_preview_lookalike_does_not_add_a_framework_requirement_to_generated_ir()
    {
        var foreign = PluginServerGenerationTestDriver.CompileReference("""
            namespace System.Runtime.Versioning;
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class RequiresPreviewFeaturesAttribute : System.Attribute { }
            """, "ForeignPreviewMetadata").WithProperties(MetadataReferenceProperties.Assembly.WithAliases(["ForeignPreview"]));
        RunGenerator("""
            extern alias ForeignPreview;
            using System;
            using DotBoxD.Abstractions;
            namespace Sample;
            [ForeignPreview::System.Runtime.Versioning.RequiresPreviewFeatures]
            public sealed record PreviewEvent(int Value);
            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null) => this;
            }
            public static class Usage
            {
                public static StepPipeline<PreviewEvent> Configure(StepPipeline<PreviewEvent> pipeline)
                    => pipeline.Where(item => item.Value >= 4);
            }
            """, [foreign], out var output, out var diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var payload = output.GetTypeByMetadataName("Sample.PreviewEvent")!;
        Assert.Equal("ForeignPreviewMetadata", Assert.Single(payload.GetAttributes()).AttributeClass!.ContainingAssembly.Name);
        var methods = output.GetSymbolsWithName(name => name == "CreateIRFunc" || name.StartsWith("Intercept_", StringComparison.Ordinal)).OfType<IMethodSymbol>().ToArray();
        Assert.Equal(2, methods.Length);
        Assert.All(methods, method => Assert.DoesNotContain(method.GetAttributes(), a =>
            a.AttributeClass?.ToDisplayString() == "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute"));
    }

}

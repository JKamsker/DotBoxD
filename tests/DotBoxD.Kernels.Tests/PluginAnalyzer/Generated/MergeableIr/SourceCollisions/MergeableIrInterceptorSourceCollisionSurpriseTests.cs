using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class MergeableIrStepGeneratorTests
{
    [Fact]
    public void Generator_does_not_leak_duplicate_type_error_when_user_declares_interceptor_helper_type()
    {
        var result = RunGenerator(
            """
            using System;
            using DotBoxD.Abstractions;

            namespace DotBoxD.Plugins.Generated
            {
                public static class MergeableIrStepInterceptors
                {
                }
            }

            namespace Sample
            {
                public sealed class StepPipeline<T>
                {
                    public StepPipeline<T> Where(
                        [LowerToIr(LoweredPipelineStepKind.Filter)] Func<T, bool> predicate)
                        => throw new InvalidOperationException("not lowered");

                    public StepPipeline<T> Where(LoweredPipelineStep step) => this;
                }

                public static class Usage
                {
                    public static StepPipeline<int> Configure(StepPipeline<int> pipeline)
                        => pipeline.Where(value => value > 0);
                }
            }
            """,
            out var outputCompilation,
            out _);

        var compilationDiagnostics = outputCompilation.GetDiagnostics();

        Assert.DoesNotContain(compilationDiagnostics, IsGeneratedInterceptorDuplicateTypeError);
        Assert.True(
            compilationDiagnostics.All(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Error) ||
            result.Diagnostics.Any(IsFocusedInterceptorDiagnostic),
            "Expected either non-colliding interceptor generation or a focused DBXK diagnostic, but got:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, compilationDiagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    [Fact]
    public void Generator_emits_interceptors_when_only_a_referenced_assembly_declares_the_helper_type()
    {
        var result = RunGenerator(
            """
            using System;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where(
                    [LowerToIr(LoweredPipelineStepKind.Filter)] Func<T, bool> predicate)
                    => throw new InvalidOperationException("not lowered");

                public StepPipeline<T> Where(LoweredPipelineStep step) => this;
            }

            public static class Usage
            {
                public static StepPipeline<int> Configure(StepPipeline<int> pipeline)
                    => pipeline.Where(value => value > 0);
            }
            """,
            [CreateInterceptorContainerReference()],
            out var outputCompilation,
            out var diagnostics);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(result.Diagnostics, IsFocusedInterceptorDiagnostic);
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("DotBoxDMergeableIrStepInterceptors", GeneratedHintNames(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_does_not_report_helper_collision_when_no_interceptors_are_generated()
    {
        var result = RunGenerator(
            """
            namespace DotBoxD.Plugins.Generated;

            public static class MergeableIrStepInterceptors
            {
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, IsFocusedInterceptorDiagnostic);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.FilePath.Contains("DotBoxDMergeableIrStepInterceptors", StringComparison.Ordinal));
    }

    private static MetadataReference CreateInterceptorContainerReference()
    {
        var compilation = CSharpCompilation.Create(
            "ReferencedInterceptorContainer",
            [CSharpSyntaxTree.ParseText(
                """
                namespace DotBoxD.Plugins.Generated;

                public static class MergeableIrStepInterceptors
                {
                }
                """)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);

        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static bool IsGeneratedInterceptorDuplicateTypeError(Diagnostic diagnostic)
        => diagnostic.Id == "CS0101" &&
           diagnostic.Location.SourceTree?.FilePath.EndsWith(
               "DotBoxDMergeableIrStepInterceptors.g.cs",
               StringComparison.Ordinal) == true;

    private static bool IsFocusedInterceptorDiagnostic(Diagnostic diagnostic)
        => diagnostic.Id == "DBXK100" &&
           diagnostic.GetMessage().Contains("interceptor", StringComparison.OrdinalIgnoreCase);
}

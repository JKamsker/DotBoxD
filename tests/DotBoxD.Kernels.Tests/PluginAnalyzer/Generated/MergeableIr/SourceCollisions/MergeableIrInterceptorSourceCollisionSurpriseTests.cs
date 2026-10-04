using Microsoft.CodeAnalysis;

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

    private static bool IsGeneratedInterceptorDuplicateTypeError(Diagnostic diagnostic)
        => diagnostic.Id == "CS0101" &&
           diagnostic.Location.SourceTree?.FilePath.EndsWith(
               "DotBoxDMergeableIrStepInterceptors.g.cs",
               StringComparison.Ordinal) == true;

    private static bool IsFocusedInterceptorDiagnostic(Diagnostic diagnostic)
        => diagnostic.Id == "DBXK100" &&
           diagnostic.GetMessage().Contains("interceptor", StringComparison.OrdinalIgnoreCase);
}

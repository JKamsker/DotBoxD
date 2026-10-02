using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class MergeableIrStepGeneratorTests
{
    private static readonly HashSet<string> s_clsDiagnosticIds = ["CS3001", "CS3002", "CS3003"];

    [Fact]
    public void Generator_does_not_leak_cls_warnings_from_mergeable_ir_helpers()
    {
        var result = RunGenerator(
            """
            using System;
            using DotBoxD.Abstractions;

            [assembly: CLSCompliant(true)]

            namespace Sample;

            internal sealed class StepPipeline<T>
            {
                internal StepPipeline<T> Where(
                    Func<T, bool> predicate,
                    [IRBodyOf(nameof(predicate))] IRFunc<T, bool>? irPredicate = null)
                    => this;
            }

            internal static class Usage
            {
                internal static StepPipeline<int> Configure(StepPipeline<int> pipeline)
                    => pipeline.Where(value => value > 0);
            }
            """,
            out var outputCompilation,
            out _);

        var dbxkDiagnostics = result.Diagnostics
            .Where(diagnostic => diagnostic.Id.StartsWith("DBXK", StringComparison.Ordinal))
            .ToArray();
        if (dbxkDiagnostics.Length > 0)
        {
            Assert.All(dbxkDiagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
            return;
        }

        var generatedClsWarnings = outputCompilation.GetDiagnostics()
            .Where(diagnostic =>
                s_clsDiagnosticIds.Contains(diagnostic.Id) &&
                diagnostic.Location.SourceTree?.FilePath.Contains("LoweredPipelineStep_", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Empty(generatedClsWarnings);
    }
}

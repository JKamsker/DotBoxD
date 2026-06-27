using System.Collections.Immutable;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class MergeableIrStepGeneratorTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);

    [Fact]
    public void Generator_lowers_marked_filter_and_projection_to_mergeable_steps()
    {
        var result = RunGeneratorAndAssertCompiles("""
            using System;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;

            namespace Sample;

            public sealed record ProbeEvent([property: Capability("probe.read.distance")] int Distance, string TargetId);

            public sealed class StepPipeline<T>
            {
                public StepPipeline<T> Where([LowerToIr(LoweredPipelineStepKind.Filter)] Func<T, bool> predicate)
                    => throw new InvalidOperationException("not lowered");

                public StepPipeline<T> Where(LoweredPipelineStep step) => this;

                public StepPipeline<TNext> Select<TNext>(
                    [LowerToIr(LoweredPipelineStepKind.Projection)] Func<T, TNext> selector)
                    => throw new InvalidOperationException("not lowered");

                public StepPipeline<TNext> Select<TNext>(LoweredPipelineStep step) => new();
            }

            public static class Usage
            {
                public static StepPipeline<string> Configure(StepPipeline<ProbeEvent> pipeline)
                    => pipeline.Where(e => e.Distance >= 4).Select(e => e.TargetId);
            }
            """);

        var generated = GeneratedSource(result);

        Assert.Contains("LoweredPipelineStepKind.Filter", generated);
        Assert.Contains("LoweredPipelineStepKind.Projection", generated);
        Assert.Contains("Var(\"$dotboxd.current\")", generated);
        Assert.Contains("\"record.get\"", generated);
        Assert.Contains("\"probe.read.distance\"", generated);
        Assert.Contains("DotBoxDMergeableIrStepInterceptors", GeneratedHintNames(result));
    }

    [Fact]
    public void Generator_reports_marked_receiver_without_lowered_step_overload()
    {
        var result = RunGenerator("""
            using System;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed class BadPipeline<T>
            {
                public BadPipeline<T> Where([LowerToIr(LoweredPipelineStepKind.Filter)] Func<T, bool> predicate)
                    => this;
            }

            public static class Usage
            {
                public static BadPipeline<int> Configure(BadPipeline<int> pipeline)
                    => pipeline.Where(value => value > 0);
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.FilePath.Contains("LoweredPipelineStep_", StringComparison.Ordinal));
    }

    private static GeneratorDriverRunResult RunGeneratorAndAssertCompiles(string source)
    {
        var result = RunGenerator(source, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Diagnostics);
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return result;
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
        => RunGenerator(source, out _, out _);

    private static GeneratorDriverRunResult RunGenerator(
        string source,
        out Compilation outputCompilation,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        var compilation = CSharpCompilation.Create(
            "DotBoxDMergeableIrStepGeneratorTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out outputCompilation, out diagnostics);
        return driver.GetRunResult();
    }

    private static string GeneratedSource(GeneratorDriverRunResult result)
        => string.Join(Environment.NewLine, result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

    private static string GeneratedHintNames(GeneratorDriverRunResult result)
        => string.Join(Environment.NewLine, result.GeneratedTrees.Select(tree => Path.GetFileName(tree.FilePath)));

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}

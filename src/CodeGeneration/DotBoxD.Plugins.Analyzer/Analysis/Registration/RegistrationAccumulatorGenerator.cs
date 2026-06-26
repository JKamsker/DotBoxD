namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class RegistrationAccumulatorGenerator
{
    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        var targetGuardedResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                RegistrationAccumulatorModelFactory.TargetAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, ct) => GeneratorGuard.TryCreate(
                    "registration accumulator target model",
                    ctx,
                    ct,
                    static (ctx, ct) => RegistrationAccumulatorModelFactory.CreateTarget(ctx, ct)));
        var rootGuardedResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                RegistrationAccumulatorModelFactory.RootAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, ct) => GeneratorGuard.TryCreate(
                    "registration accumulator root model",
                    ctx,
                    ct,
                    static (ctx, ct) => RegistrationAccumulatorModelFactory.CreateRoot(ctx, ct)));
        GeneratorGuard.RegisterDiagnostics(context, targetGuardedResults);
        GeneratorGuard.RegisterDiagnostics(context, rootGuardedResults);
        var targetResults = GeneratorGuard.Values(targetGuardedResults);
        var rootResults = GeneratorGuard.Values(rootGuardedResults);

        RegisterDiagnostics(context, targetResults);
        RegisterDiagnostics(context, rootResults);

        var targets = targetResults
            .Where(static result => result.Target is not null)
            .Select(static (result, _) => result.Target!);
        var targetBatchResults = targets
            .Collect()
            .Select(static (items, ct) => GeneratorGuard.TryTransform(
                "registration accumulator target batch",
                items,
                ct,
                static (items, _) => RegistrationAccumulatorBatchFactory.CreateTargets(items)));
        GeneratorGuard.RegisterDiagnostics(context, targetBatchResults);
        var targetBatch = GeneratorGuard.ValueOrDefault(targetBatchResults);
        context.RegisterSourceOutput(targetBatch, static (sourceContext, batch) =>
            GeneratorGuard.TryEmit(
                sourceContext,
                "registration accumulator target source output",
                batch,
                static (sourceContext, batch) => EmitBatch(sourceContext, batch)));

        var roots = rootResults
            .Where(static result => result.Root is not null)
            .Select(static (result, _) => result.Root!);
        var rootBatchResults = roots
            .Collect()
            .Combine(targets.Collect())
            .Select(static (pair, ct) => GeneratorGuard.TryTransform(
                "registration accumulator root batch",
                pair,
                ct,
                static (pair, _) => RegistrationAccumulatorBatchFactory.CreateRoots(pair.Left, pair.Right)));
        GeneratorGuard.RegisterDiagnostics(context, rootBatchResults);
        var rootBatch = GeneratorGuard.ValueOrDefault(rootBatchResults);
        context.RegisterSourceOutput(rootBatch, static (sourceContext, batch) =>
            GeneratorGuard.TryEmit(
                sourceContext,
                "registration accumulator root source output",
                batch,
                static (sourceContext, batch) => EmitBatch(sourceContext, batch)));
    }

    private static void RegisterDiagnostics(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<RegistrationAccumulatorGenerationResult> results)
        => GeneratorGuard.RegisterOutput(
            context,
            results.Where(static result => result.Diagnostic is not null).Select(static (result, _) => result.Diagnostic!),
            "registration accumulator diagnostic output",
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));

    private static void EmitBatch(SourceProductionContext context, RegistrationGenerationBatch batch)
    {
        foreach (var diagnostic in batch.Diagnostics)
        {
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
        }

        foreach (var source in batch.Sources)
        {
            context.AddSource(source.HintName, source.Source);
        }
    }
}

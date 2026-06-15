namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class RegistrationAccumulatorGenerator
{
    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        var targetResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                RegistrationAccumulatorModelFactory.TargetAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, ct) => RegistrationAccumulatorModelFactory.CreateTarget(ctx, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);
        var rootResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                RegistrationAccumulatorModelFactory.RootAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, ct) => RegistrationAccumulatorModelFactory.CreateRoot(ctx, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        RegisterDiagnostics(context, targetResults);
        RegisterDiagnostics(context, rootResults);

        var targets = targetResults
            .Where(static result => result.Target is not null)
            .Select(static (result, _) => result.Target!);
        var targetBatch = targets
            .Collect()
            .Select(static (items, _) => RegistrationAccumulatorBatchFactory.CreateTargets(items));
        context.RegisterSourceOutput(targetBatch, static (sourceContext, batch) =>
            EmitBatch(sourceContext, batch));

        var roots = rootResults
            .Where(static result => result.Root is not null)
            .Select(static (result, _) => result.Root!);
        var rootBatch = roots
            .Collect()
            .Combine(targets.Collect())
            .Select(static (pair, _) => RegistrationAccumulatorBatchFactory.CreateRoots(pair.Left, pair.Right));
        context.RegisterSourceOutput(rootBatch, static (sourceContext, batch) =>
            EmitBatch(sourceContext, batch));
    }

    private static void RegisterDiagnostics(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<RegistrationAccumulatorGenerationResult> results)
        => context.RegisterSourceOutput(
            results.Where(static result => result.Diagnostic is not null).Select(static (result, _) => result.Diagnostic!),
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

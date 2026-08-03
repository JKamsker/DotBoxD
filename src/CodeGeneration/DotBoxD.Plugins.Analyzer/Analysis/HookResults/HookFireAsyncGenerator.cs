using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

internal static class HookFireAsyncGenerator
{
    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        var results = GeneratorGuard.AttributeValues(
            context,
            DotBoxDMetadataNames.HookAttribute,
            static (node, _) => node is TypeDeclarationSyntax,
            "hook FireAsync extension model",
            static (ctx, ct) => HookFireAsyncModelFactory.Create(ctx, ct));
        GeneratorGuard.RegisterOutput(
            context,
            results
                .Where(static result => result.Diagnostic is not null)
                .Select(static (result, _) => result.Diagnostic!),
            "hook FireAsync extension diagnostic output",
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));

        var models = results
            .Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .Collect();
        var sourceCollisions = HookFireAsyncSourceCollision.Collect(context);
        GeneratorGuard.RegisterOutput(
            context,
            models.Combine(sourceCollisions),
            "hook FireAsync extension source collision diagnostic output",
            static (sourceContext, pair) => HookFireAsyncSourceCollision.Report(sourceContext, pair.Left, pair.Right));
        GeneratorGuard.RegisterOutput(
            context,
            models.Combine(sourceCollisions),
            "hook FireAsync extension source output",
            static (sourceContext, pair) => HookFireAsyncExtensionEmitter.Emit(sourceContext, pair.Left, pair.Right));
    }
}

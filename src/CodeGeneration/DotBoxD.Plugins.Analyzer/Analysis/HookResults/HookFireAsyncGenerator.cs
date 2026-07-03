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
        GeneratorGuard.RegisterOutput(
            context,
            models,
            "hook FireAsync extension source output",
            static (sourceContext, models) => HookFireAsyncExtensionEmitter.Emit(sourceContext, models));
    }
}

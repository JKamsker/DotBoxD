namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[Generator(LanguageNames.CSharp)]
public sealed class SafeIrPluginPackageGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var packages = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "SafeIR.Plugins.GamePluginAttribute",
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => PluginKernelModelFactory.Create(ctx, ct))
            .Where(static result => result is not null);

        context.RegisterSourceOutput(packages, static (context, result) => {
            if (result is null) {
                return;
            }

            if (result.Diagnostic is not null) {
                context.ReportDiagnostic(result.Diagnostic);
                return;
            }

            if (result.Package is not null) {
                context.AddSource(result.Package.HintName, result.Package.Source);
            }
        });
    }
}

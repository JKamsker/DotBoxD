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
            .Where(static package => package is not null);

        context.RegisterSourceOutput(packages, static (context, package) => {
            if (package is null) {
                return;
            }

            context.AddSource(package.HintName, package.Source);
        });
    }
}

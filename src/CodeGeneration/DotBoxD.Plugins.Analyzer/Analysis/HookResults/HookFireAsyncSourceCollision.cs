using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

internal static class HookFireAsyncSourceCollision
{
    private const string RuntimeNamespace = "DotBoxD.Plugins.Runtime";
    private const string HelperTypeName = "HookRegistryFireAsyncExtensions";

    public static IncrementalValueProvider<ImmutableArray<PluginDiagnosticLocation>> Collect(
        IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => CouldCollide(node),
                static (ctx, ct) => Location(ctx, ct))
            .Where(static location => location.HasValue)
            .Select(static (location, _) => location!.Value)
            .Collect();

    public static void Report(
        SourceProductionContext context,
        ImmutableArray<HookFireAsyncModel> models,
        ImmutableArray<PluginDiagnosticLocation> collisions)
    {
        if (models.IsDefaultOrEmpty || collisions.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var collision in collisions)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
                collision.ToLocation(),
                "Generated FireAsync extension type 'DotBoxD.Plugins.Runtime.HookRegistryFireAsyncExtensions' "
                + "collides with an existing source type; rename the existing type or call "
                + "HookRegistry.FireAsync<TContext, TResult>(...) directly."));
        }
    }

    private static bool CouldCollide(SyntaxNode node)
        => node switch
        {
            BaseTypeDeclarationSyntax declaration => IsHelperName(declaration.Identifier),
            DelegateDeclarationSyntax declaration => IsHelperName(declaration.Identifier),
            _ => false
        };

    private static bool IsHelperName(SyntaxToken identifier)
        => string.Equals(identifier.ValueText, HelperTypeName, StringComparison.Ordinal);

    private static PluginDiagnosticLocation? Location(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not INamedTypeSymbol
            {
                ContainingType: null
            } type ||
            !string.Equals(type.MetadataName, HelperTypeName, StringComparison.Ordinal) ||
            !string.Equals(type.ContainingNamespace.ToDisplayString(), RuntimeNamespace, StringComparison.Ordinal))
        {
            return null;
        }

        return PluginDiagnosticLocation.From(type.Locations[0]);
    }
}

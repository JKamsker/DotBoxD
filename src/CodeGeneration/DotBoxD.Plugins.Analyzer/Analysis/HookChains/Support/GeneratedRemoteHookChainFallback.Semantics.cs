using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static ITypeSymbol? TypeFromSyntax(
        TypeSyntax typeSyntax,
        SemanticModel model,
        CancellationToken cancellationToken)
        => SemanticModelFor(typeSyntax, model)?.GetTypeInfo(typeSyntax, cancellationToken).Type;

    private static SemanticModel? SemanticModelFor(SyntaxNode node, SemanticModel model)
    {
        if (ReferenceEquals(node.SyntaxTree, model.SyntaxTree))
        {
            return model;
        }

        foreach (var tree in model.Compilation.SyntaxTrees)
        {
            if (ReferenceEquals(tree, node.SyntaxTree))
            {
                return model.Compilation.GetSemanticModel(tree);
            }
        }

        return null;
    }
}

using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainSemanticModelResolver
{
    public static SemanticModel? For(SyntaxNode node, SemanticModel model)
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

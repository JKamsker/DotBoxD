using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal readonly struct HookChainStage
{
    public HookChainStage(bool isSelect, LambdaExpressionSyntax lambda)
    {
        IsSelect = isSelect;
        Lambda = lambda;
    }

    public bool IsSelect { get; }

    public LambdaExpressionSyntax Lambda { get; }
}

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal readonly struct HookChainStage
{
    public HookChainStage(bool isSelect, InvocationExpressionSyntax invocation, LambdaExpressionSyntax lambda)
    {
        IsSelect = isSelect;
        Invocation = invocation;
        Lambda = lambda;
    }

    public bool IsSelect { get; }

    public InvocationExpressionSyntax Invocation { get; }

    public LambdaExpressionSyntax Lambda { get; }
}

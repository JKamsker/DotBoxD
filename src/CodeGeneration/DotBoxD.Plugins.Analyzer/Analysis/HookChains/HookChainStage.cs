using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
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

internal sealed record HookChainProjection(
    DotBoxDStatementBodyModel? Prefix,
    DotBoxDExpressionModel Value,
    ITypeSymbol? ValueType);

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal readonly record struct HookChainStage(bool IsSelect, LambdaExpressionSyntax Lambda);

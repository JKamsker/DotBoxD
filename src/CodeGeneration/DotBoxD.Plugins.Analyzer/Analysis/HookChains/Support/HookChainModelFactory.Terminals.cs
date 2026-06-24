using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static bool TerminalReturnsVoid(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (lambda.ExpressionBody is not { } body)
        {
            return lambda.Body is BlockSyntax block && BlockReturnsVoid(lambda, block);
        }

        if (model.GetTypeInfo(body, cancellationToken).Type?.SpecialType == SpecialType.System_Void)
        {
            return true;
        }

        return model.GetSymbolInfo(body, cancellationToken).Symbol is IMethodSymbol { ReturnsVoid: true };
    }

    private static bool BlockReturnsVoid(LambdaExpressionSyntax lambda, BlockSyntax block)
    {
        if (lambda.AsyncKeyword.RawKind != 0)
        {
            return false;
        }

        return !block.DescendantNodes(static node =>
                node is not LambdaExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<ReturnStatementSyntax>()
            .Any(static returned => returned.Expression is not null);
    }
}

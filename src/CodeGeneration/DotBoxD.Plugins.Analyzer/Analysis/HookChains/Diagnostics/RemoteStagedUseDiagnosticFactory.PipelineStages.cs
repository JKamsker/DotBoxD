using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class RemoteStagedUseDiagnosticFactory
{
    private static bool IsPipelineStageInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(invocation, cancellationToken);
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
        var method = symbol as IMethodSymbol;
        return PipelineRoleReader.RoleOf(method, model.Compilation) is
            PipelineCallRole.Filter or PipelineCallRole.Projection ||
            GeneratedRemoteHookChainFallback.RoleOfUnresolvedGeneratedSurface(
                invocation,
                model,
                cancellationToken,
                method) is
            PipelineCallRole.Filter or PipelineCallRole.Projection;
    }

    private static bool ContainsPipelineStageInvocation(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (depth > 8)
        {
            return false;
        }

        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        var expressionModel = HookChainSemanticModelResolver.For(expression, model);
        if (expressionModel is null)
        {
            return false;
        }

        if (expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax stageAccess
            } invocation)
        {
            return IsPipelineStageInvocation(invocation, expressionModel, cancellationToken) ||
                ContainsPipelineStageInvocation(stageAccess.Expression, expressionModel, cancellationToken, depth + 1);
        }

        return expression is MemberAccessExpressionSyntax access &&
            ContainsPipelineStageInvocation(access.Expression, expressionModel, cancellationToken, depth + 1);
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static bool HasPriorDiscardedStageOnReceiver(
        ExpressionSyntax receiver,
        InvocationExpressionSyntax terminal,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!TryGetReceiverScanContext(receiver, terminal, model, cancellationToken, out var receiverLocal, out var block))
        {
            return false;
        }

        foreach (var invocation in block.DescendantNodes(static node =>
                node is not LambdaExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (invocation.SpanStart >= terminal.SpanStart)
            {
                continue;
            }

            if (!IsStageInvocationOnReceiver(invocation, receiverLocal, model, cancellationToken))
            {
                continue;
            }

            if (StageMutatesReceiver(invocation, receiverLocal, model, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetReceiverScanContext(
        ExpressionSyntax receiver,
        InvocationExpressionSyntax terminal,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ILocalSymbol receiverLocal,
        out BlockSyntax block)
    {
        receiverLocal = null!;
        block = null!;
        var unwrappedReceiver = HookChainAliasResolver.UnwrapTransparentExpression(receiver);
        if (unwrappedReceiver is not IdentifierNameSyntax receiverIdentifier)
        {
            return false;
        }

        if (model.GetSymbolInfo(receiverIdentifier, cancellationToken).Symbol is not ILocalSymbol local)
        {
            return false;
        }

        if (terminal.FirstAncestorOrSelf<BlockSyntax>() is not { } containingBlock)
        {
            return false;
        }

        receiverLocal = local;
        block = containingBlock;
        return true;
    }

    private static bool IsStageInvocationOnReceiver(
        InvocationExpressionSyntax invocation,
        ILocalSymbol receiverLocal,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return false;
        }

        if (RoleOf(invocation, model, cancellationToken) is not
            (PipelineCallRole.Filter or PipelineCallRole.Projection))
        {
            return false;
        }

        if (!IsHookStageInvocation(invocation, model, cancellationToken))
        {
            return false;
        }

        return ExpressionReferencesLocal(access.Expression, receiverLocal, model, cancellationToken, depth: 0);
    }

    private static bool StageMutatesReceiver(
        InvocationExpressionSyntax invocation,
        ILocalSymbol receiverLocal,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (IsDiscardedStage(invocation))
        {
            return true;
        }

        return IsAssignedBackToReceiver(invocation, receiverLocal, model, cancellationToken);
    }

    private static bool IsDiscardedStage(InvocationExpressionSyntax invocation)
        => TransparentStageExpression(invocation).Parent is ExpressionStatementSyntax;

    private static bool IsHookStageInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
        => model.GetTypeInfo(invocation, cancellationToken).Type is INamedTypeSymbol type &&
           ReceiverKind(type, model.Compilation) is not null;

    private static bool IsAssignedBackToReceiver(
        InvocationExpressionSyntax invocation,
        ILocalSymbol receiverLocal,
        SemanticModel model,
        CancellationToken cancellationToken)
        => TransparentStageExpression(invocation).Parent is AssignmentExpressionSyntax assignment &&
           assignment.Parent is ExpressionStatementSyntax &&
           model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is ILocalSymbol assigned &&
           SymbolEqualityComparer.Default.Equals(assigned, receiverLocal);

    private static ExpressionSyntax TransparentStageExpression(ExpressionSyntax expression)
    {
        while (expression.Parent is ParenthesizedExpressionSyntax parenthesized &&
               parenthesized.Expression == expression ||
               expression.Parent is PostfixUnaryExpressionSyntax postfix &&
               postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) &&
               postfix.Operand == expression)
        {
            expression = (ExpressionSyntax)expression.Parent;
        }

        return expression;
    }

    private static bool ExpressionReferencesLocal(
        ExpressionSyntax expression,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (depth > HookChainAliasResolver.MaxResolutionDepth)
        {
            return false;
        }

        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        if (expression is IdentifierNameSyntax identifier &&
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, local))
        {
            return true;
        }

        if (HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer &&
            ExpressionReferencesLocal(initializer, local, model, cancellationToken, depth + 1))
        {
            return true;
        }

        if (expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax chained
            })
        {
            return ExpressionReferencesLocal(chained.Expression, local, model, cancellationToken, depth + 1);
        }

        return expression is MemberAccessExpressionSyntax access &&
            ExpressionReferencesLocal(access.Expression, local, model, cancellationToken, depth + 1);
    }
}

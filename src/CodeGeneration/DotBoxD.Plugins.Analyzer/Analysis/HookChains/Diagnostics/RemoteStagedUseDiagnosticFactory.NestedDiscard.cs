using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class RemoteStagedUseDiagnosticFactory
{
    private static bool TryCreateNestedDiscardDiagnostic(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken,
        ExpressionSyntax transparentExpression,
        out PluginKernelDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (transparentExpression.Parent is null ||
            IsFluentContinuationReceiver(transparentExpression) ||
            IsReturnedOrDeferredExpressionBody(transparentExpression) ||
            EnclosingLocalFlowsIntoTerminalOrUse(invocation, transparentExpression, model, cancellationToken))
        {
            return false;
        }

        var receiverType = model.GetTypeInfo(access.Expression, cancellationToken).Type;
        if (!IsRemoteChainOrStageType(receiverType) &&
            !IsGeneratedRemoteChain(access.Expression, model, cancellationToken))
        {
            return false;
        }

        diagnostic = new PluginKernelDiagnostic(
            DiscardedStageMessage,
            PluginDiagnosticLocation.From(access.Name.GetLocation()));
        return true;
    }

    private static bool IsFluentContinuationReceiver(ExpressionSyntax expression)
    {
        if (expression.Parent is not MemberAccessExpressionSyntax access ||
            access.Expression != expression ||
            access.Parent is not InvocationExpressionSyntax)
        {
            return false;
        }

        return access.Name.Identifier.ValueText is
            "Where" or
            "Select" or
            "Run" or
            "RunLocal" or
            "Register" or
            "RegisterLocal" or
            "Use" or
            "UseGeneratedChain" or
            "UseGeneratedLocalChain" or
            "UseGeneratedResultChain" or
            "UseGeneratedLocalResultChain";
    }

    private static bool IsReturnedOrDeferredExpressionBody(ExpressionSyntax expression)
        => expression.Parent is ReturnStatementSyntax ||
            expression.Parent is ArrowExpressionClauseSyntax ||
            expression.Parent is LambdaExpressionSyntax;

    private static bool EnclosingLocalFlowsIntoTerminalOrUse(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var declarator = expression.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        return declarator is not null &&
            model.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol local &&
            RemoteStagedUseFlowAnalyzer.LocalFlowsIntoTerminalOrUse(invocation, local, model, cancellationToken);
    }
}

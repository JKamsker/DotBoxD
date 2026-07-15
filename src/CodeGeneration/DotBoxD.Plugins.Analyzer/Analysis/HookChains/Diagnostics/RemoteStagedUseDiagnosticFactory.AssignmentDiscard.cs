using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class RemoteStagedUseDiagnosticFactory
{
    private static bool TryHandleAssignmentDiscard(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken,
        ExpressionSyntax transparentExpression,
        ref ExpressionSyntax discardedExpression,
        out PluginKernelDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (transparentExpression.Parent is not AssignmentExpressionSyntax assignment ||
            assignment.Right != transparentExpression ||
            assignment.Parent is not ExpressionStatementSyntax)
        {
            return false;
        }

        var assignsLocal = model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is ILocalSymbol;
        if (CreateAssignedStageDiagnostic(invocation, assignment, access, model, cancellationToken) is { } assigned)
        {
            diagnostic = assigned;
            return true;
        }

        if (assignsLocal)
        {
            return true;
        }

        discardedExpression = assignment;
        return false;
    }

    private static bool TryCreateLocalDeclarationDiagnostic(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken,
        ExpressionSyntax transparentExpression,
        out PluginKernelDiagnostic? diagnostic)
    {
        diagnostic = null;
        if (transparentExpression.Parent is not EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax declarator
            } ||
            model.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol local)
        {
            return false;
        }

        diagnostic = CreateStagedLocalDiagnostic(invocation, access, model, local, cancellationToken);
        return true;
    }

    private static PluginKernelDiagnostic? CreateStagedLocalDiagnostic(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        ILocalSymbol local,
        CancellationToken cancellationToken)
    {
        var receiverType = model.GetTypeInfo(access.Expression, cancellationToken).Type;
        if ((!IsRemoteChainOrStageType(receiverType) &&
             !IsGeneratedRemoteChain(access.Expression, model, cancellationToken)) ||
            RemoteStagedUseFlowAnalyzer.LocalFlowsIntoTerminalOrUse(invocation, local, model, cancellationToken))
        {
            return null;
        }

        return new PluginKernelDiagnostic(
            DiscardedStageMessage,
            PluginDiagnosticLocation.From(access.Name.GetLocation()));
    }

    private static PluginKernelDiagnostic? CreateAssignedStageDiagnostic(
        InvocationExpressionSyntax invocation,
        AssignmentExpressionSyntax assignment,
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var receiverType = model.GetTypeInfo(access.Expression, cancellationToken).Type;
        if (!IsRemoteChainOrStageType(receiverType) &&
            !IsGeneratedRemoteChain(access.Expression, model, cancellationToken))
        {
            return null;
        }

        if (model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not ILocalSymbol local ||
            !RemoteStagedUseFlowAnalyzer.LocalFlowsIntoTerminalOrUse(invocation, local, model, cancellationToken))
        {
            return null;
        }

        return new PluginKernelDiagnostic(
            AssignedStageMessage,
            PluginDiagnosticLocation.From(access.Name.GetLocation()));
    }
}

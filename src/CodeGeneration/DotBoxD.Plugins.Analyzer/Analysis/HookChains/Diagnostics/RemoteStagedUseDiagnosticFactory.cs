using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class RemoteStagedUseDiagnosticFactory
{
    private const string TerminalMessagePrefix =
        "Remote Where/Select stages only lower when the terminal is Run, RunLocal, Register, or RegisterLocal; " +
        "calling ";
    private const string DiscardedStageMessage =
        "Remote Where/Select stages must be chained into Run, RunLocal, Register, or RegisterLocal; " +
        "discarding the stage result would ignore the stage.";
    private const string AssignedStageMessage =
        "Remote Where/Select stages assigned to an existing local are not lowered into a later terminal; " +
        "keep the stage in the fluent chain or initialize a new local with the staged expression.";

    public static bool IsCandidate(SyntaxNode node)
        => node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Use"
                    or "UseGeneratedChain"
                    or "UseGeneratedLocalChain"
                    or "UseGeneratedResultChain"
                    or "UseGeneratedLocalResultChain"
                    or "Where"
                    or "Select"
            }
        };

    public static PluginKernelDiagnostic? Create(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return null;
        }

        if (access.Name.Identifier.ValueText is "Where" or "Select")
        {
            return CreateDiscardedStageDiagnostic(invocation, access, context.SemanticModel, cancellationToken);
        }

        var receiverType = context.SemanticModel.GetTypeInfo(access.Expression, cancellationToken).Type;
        if (!ContainsStageInvocationOrAlias(access.Expression, context.SemanticModel, cancellationToken) &&
            !IsRemoteStageType(receiverType))
        {
            return null;
        }

        if (!IsRemoteChainOrStageType(receiverType) &&
            !IsGeneratedRemoteChain(access.Expression, context.SemanticModel, cancellationToken))
        {
            return null;
        }

        return new PluginKernelDiagnostic(
            UnsupportedTerminalMessage(access.Name.Identifier.ValueText),
            PluginDiagnosticLocation.From(access.Name.GetLocation()));
    }

    private static string UnsupportedTerminalMessage(string terminal)
        => TerminalMessagePrefix + terminal + " after Where/Select would ignore those stages.";

    private static PluginKernelDiagnostic? CreateDiscardedStageDiagnostic(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var transparentExpression = UnwrapTransparentParent(invocation);
        ExpressionSyntax discardedExpression = transparentExpression;
        if (TryHandleAssignmentDiscard(
                invocation,
                access,
                model,
                cancellationToken,
                transparentExpression,
                ref discardedExpression,
                out var assignmentDiagnostic))
        {
            return assignmentDiagnostic;
        }

        if (TryCreateLocalDeclarationDiagnostic(
                invocation,
                access,
                model,
                cancellationToken,
                transparentExpression,
                out var localDiagnostic))
        {
            return localDiagnostic;
        }

        if (TryCreateNestedDiscardDiagnostic(
                invocation,
                access,
                model,
                cancellationToken,
                transparentExpression,
                out var nestedDiagnostic))
        {
            return nestedDiagnostic;
        }

        if (discardedExpression.Parent is not ExpressionStatementSyntax)
        {
            return null;
        }

        var receiverType = model.GetTypeInfo(access.Expression, cancellationToken).Type;
        if (!IsRemoteChainOrStageType(receiverType) &&
            !IsGeneratedRemoteChain(access.Expression, model, cancellationToken))
        {
            return null;
        }

        return new PluginKernelDiagnostic(
            DiscardedStageMessage,
            PluginDiagnosticLocation.From(access.Name.GetLocation()));
    }

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

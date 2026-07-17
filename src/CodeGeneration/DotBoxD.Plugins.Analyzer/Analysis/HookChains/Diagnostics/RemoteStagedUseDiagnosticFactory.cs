using Microsoft.CodeAnalysis;
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
        } ||
            node is InvocationExpressionSyntax invocation && IsStoredInvocation(invocation) ||
            node is ConditionalAccessExpressionSyntax
            {
                WhenNotNull: InvocationExpressionSyntax
                {
                    Expression: MemberBindingExpressionSyntax
                    {
                        Name.Identifier.ValueText: "Where" or "Select"
                    }
                }
            };

    public static PluginKernelDiagnostic? Create(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        if (context.Node is ConditionalAccessExpressionSyntax conditional)
        {
            return CreateConditionalDiscardedStageDiagnostic(conditional, context.SemanticModel, cancellationToken);
        }

        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return CreateStoredReturnedStageDiagnostic(invocation, context.SemanticModel, cancellationToken);
        }

        if (access.Name.Identifier.ValueText is "Where" or "Select")
        {
            return CreateDiscardedStageDiagnostic(invocation, access, context.SemanticModel, cancellationToken);
        }

        if (CreateStoredReturnedStageDiagnostic(invocation, context.SemanticModel, cancellationToken) is { } stored)
        {
            return stored;
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

    private static PluginKernelDiagnostic? CreateConditionalDiscardedStageDiagnostic(
        ConditionalAccessExpressionSyntax conditional,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (conditional.WhenNotNull is not InvocationExpressionSyntax
            {
                Expression: MemberBindingExpressionSyntax binding
            })
        {
            return null;
        }

        var transparentExpression = UnwrapTransparentParent(conditional);
        if (transparentExpression.Parent is not ExpressionStatementSyntax)
        {
            return null;
        }

        var receiverType = model.GetTypeInfo(conditional.Expression, cancellationToken).Type;
        if (!IsRemoteChainOrStageType(receiverType) &&
            !IsGeneratedRemoteChain(conditional.Expression, model, cancellationToken))
        {
            return null;
        }

        return new PluginKernelDiagnostic(
            DiscardedStageMessage,
            PluginDiagnosticLocation.From(binding.Name.GetLocation()));
    }

    private static string UnsupportedTerminalMessage(string terminal)
        => TerminalMessagePrefix + terminal + " after Where/Select would ignore those stages.";

    private static bool IsStoredInvocation(InvocationExpressionSyntax invocation)
    {
        var transparentExpression = UnwrapTransparentParent(invocation);
        return transparentExpression.Parent is EqualsValueClauseSyntax
        {
            Parent: VariableDeclaratorSyntax
        } ||
            transparentExpression.Parent is AssignmentExpressionSyntax
            {
                Right: var right
            } &&
            right == transparentExpression;
    }

    private static PluginKernelDiagnostic? CreateStoredReturnedStageDiagnostic(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var transparentExpression = UnwrapTransparentParent(invocation);
        if (!TryStoredLocal(transparentExpression, model, cancellationToken, out var local) ||
            !ContainsStageInvocationOrAlias(invocation, model, cancellationToken) ||
            !IsRemoteChainOrStageType(model.GetTypeInfo(invocation, cancellationToken).Type))
        {
            return null;
        }

        if (local is not null &&
            RemoteStagedUseFlowAnalyzer.LocalFlowsIntoTerminalOrUse(invocation, local, model, cancellationToken))
        {
            return null;
        }

        return new PluginKernelDiagnostic(
            DiscardedStageMessage,
            PluginDiagnosticLocation.From(invocation.Expression.GetLocation()));
    }

    private static bool TryStoredLocal(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ILocalSymbol? local)
    {
        local = null;
        if (expression.Parent is EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax declarator
            })
        {
            local = model.GetDeclaredSymbol(declarator, cancellationToken) as ILocalSymbol;
            return true;
        }

        if (expression.Parent is not AssignmentExpressionSyntax assignment ||
            assignment.Right != expression)
        {
            return false;
        }

        local = model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol as ILocalSymbol;
        return true;
    }

    private static PluginKernelDiagnostic? CreateDiscardedStageDiagnostic(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!IsPipelineStageInvocation(invocation, model, cancellationToken))
        {
            return null;
        }

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

}

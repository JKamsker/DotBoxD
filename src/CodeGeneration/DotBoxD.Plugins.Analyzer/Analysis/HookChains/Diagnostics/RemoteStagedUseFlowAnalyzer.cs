using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class RemoteStagedUseFlowAnalyzer
{
    private static readonly HashSet<string> UseNames = new(StringComparer.Ordinal)
    {
        "Use",
        "UseGeneratedChain",
        "UseGeneratedLocalChain",
        "UseGeneratedResultChain",
        "UseGeneratedLocalResultChain",
    };

    public static bool LocalFlowsIntoTerminalOrUse(
        InvocationExpressionSyntax invocation,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var block = invocation.FirstAncestorOrSelf<BlockSyntax>();
        if (block is null)
        {
            return false;
        }

        return LocalFlowsIntoSupportedTerminal(block, invocation, local, model, cancellationToken) ||
            LocalFlowsIntoReturnedExpression(block, invocation, local, model, cancellationToken);
    }

    public static bool ContainsStageInvocation(ExpressionSyntax expression)
    {
        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);

        if (expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax stageAccess
            })
        {
            var stageName = stageAccess.Name.Identifier.ValueText;
            return stageName is "Where" or "Select" ||
                ContainsStageInvocation(stageAccess.Expression);
        }

        return expression is MemberAccessExpressionSyntax access &&
            ContainsStageInvocation(access.Expression);
    }

    private static bool LocalFlowsIntoSupportedTerminal(
        BlockSyntax block,
        InvocationExpressionSyntax invocation,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var terminal in block.DescendantNodes(static node =>
                node is not LambdaExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsPriorOrCurrentInvocation(terminal, invocation))
            {
                continue;
            }

            if (!TrySupportedTerminalReceiver(terminal, model, cancellationToken, out var terminalReceiver))
            {
                continue;
            }

            if (HasMutationBeforeTerminal(local, invocation, terminal, model, cancellationToken))
            {
                continue;
            }

            if (ExpressionReferencesLocal(terminalReceiver, local, model, cancellationToken, depth: 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LocalFlowsIntoReturnedExpression(
        BlockSyntax block,
        InvocationExpressionSyntax invocation,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var returned in block.DescendantNodes(static node =>
                node is not LambdaExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<ReturnStatementSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (returned.SpanStart <= invocation.SpanStart || returned.Expression is null)
            {
                continue;
            }

            if (!HasMutationBeforeReturn(local, invocation, returned, model, cancellationToken) &&
                ExpressionReferencesLocal(returned.Expression, local, model, cancellationToken, depth: 0))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPriorOrCurrentInvocation(
        InvocationExpressionSyntax terminal,
        InvocationExpressionSyntax invocation)
        => terminal == invocation || terminal.SpanStart <= invocation.SpanStart;

    private static bool TrySupportedTerminalReceiver(
        InvocationExpressionSyntax terminal,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ExpressionSyntax terminalReceiver)
    {
        if (TryTerminalReceiver(terminal, out var terminalName, out terminalReceiver) &&
            (IsPipelineTerminal(terminal, model, cancellationToken) || UseNames.Contains(terminalName)))
        {
            return true;
        }

        terminalReceiver = null!;
        return false;
    }

    private static bool IsPipelineTerminal(
        InvocationExpressionSyntax terminal,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(terminal, cancellationToken);
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
        return PipelineRoleReader.RoleOf(symbol as IMethodSymbol, model.Compilation) is
            PipelineCallRole.Run or
            PipelineCallRole.RunLocal or
            PipelineCallRole.Register or
            PipelineCallRole.RegisterLocal;
    }

    private static bool TryTerminalReceiver(
        InvocationExpressionSyntax invocation,
        out string terminalName,
        out ExpressionSyntax receiver)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax access)
        {
            terminalName = access.Name.Identifier.ValueText;
            receiver = access.Expression;
            return true;
        }

        if (invocation.Expression is MemberBindingExpressionSyntax binding &&
            invocation.Parent is ConditionalAccessExpressionSyntax conditional &&
            conditional.WhenNotNull == invocation)
        {
            terminalName = binding.Name.Identifier.ValueText;
            receiver = conditional.Expression;
            return true;
        }

        terminalName = string.Empty;
        receiver = invocation;
        return false;
    }

    private static bool HasMutationBeforeTerminal(
        ILocalSymbol local,
        InvocationExpressionSyntax invocation,
        InvocationExpressionSyntax terminal,
        SemanticModel model,
        CancellationToken cancellationToken)
        => HookChainAliasResolver.HasMutationBetween(
            local,
            invocation.SpanStart,
            terminal.SpanStart,
            model,
            cancellationToken);

    private static bool HasMutationBeforeReturn(
        ILocalSymbol local,
        InvocationExpressionSyntax invocation,
        ReturnStatementSyntax returned,
        SemanticModel model,
        CancellationToken cancellationToken)
        => HookChainAliasResolver.HasMutationBetween(
            local,
            invocation.SpanStart,
            returned.SpanStart,
            model,
            cancellationToken);

    private static bool ExpressionReferencesLocal(
        ExpressionSyntax expression,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (depth > 8)
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

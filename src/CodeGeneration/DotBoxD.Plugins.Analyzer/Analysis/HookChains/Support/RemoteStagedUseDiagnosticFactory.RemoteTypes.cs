using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class RemoteStagedUseDiagnosticFactory
{
    private static bool ContainsStageInvocationOrAlias(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => ContainsStageInvocationOrAlias(expression, model, cancellationToken, depth: 0);

    private static bool ContainsStageInvocationOrAlias(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (depth > 8)
        {
            return false;
        }

        if (RemoteStagedUseFlowAnalyzer.ContainsStageInvocation(expression))
        {
            return true;
        }

        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        var expressionModel = HookChainSemanticModelResolver.For(expression, model);
        if (expressionModel is null)
        {
            return false;
        }

        if (expression is ConditionalExpressionSyntax conditional)
        {
            return ContainsStageInvocationOrAlias(conditional.WhenTrue, expressionModel, cancellationToken, depth + 1) ||
                ContainsStageInvocationOrAlias(conditional.WhenFalse, expressionModel, cancellationToken, depth + 1);
        }

        if (ReturnedExpression(expression, expressionModel, cancellationToken) is { } returned)
        {
            return ContainsStageInvocationOrAlias(returned, expressionModel, cancellationToken, depth + 1);
        }

        return HookChainAliasResolver.Initializer(expression, expressionModel, cancellationToken) is { } initializer &&
            ContainsStageInvocationOrAlias(initializer, expressionModel, cancellationToken, depth + 1);
    }

    private static ExpressionSyntax? ReturnedExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        var expressionModel = HookChainSemanticModelResolver.For(expression, model);
        if (expressionModel is null ||
            InvokedMethod(expression, expressionModel, cancellationToken) is not { } method)
        {
            return null;
        }

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = reference.GetSyntax(cancellationToken);
            var expressionBody = ExpressionBody(syntax);
            if (expressionBody is not null)
            {
                return expressionBody;
            }

            var body = BlockBody(syntax);
            if (body is null)
            {
                continue;
            }

            if (SingleReturnExpression(body) is { } returned)
            {
                return returned;
            }
        }

        return null;
    }

    private static IMethodSymbol? InvokedMethod(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (expression is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        return HookChainSemanticModelResolver.For(invocation, model)?.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
    }

    private static ExpressionSyntax? ExpressionBody(SyntaxNode syntax)
        => syntax switch
        {
            MethodDeclarationSyntax methodDeclaration => methodDeclaration.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax localFunction => localFunction.ExpressionBody?.Expression,
            _ => null
        };

    private static BlockSyntax? BlockBody(SyntaxNode syntax)
        => syntax switch
        {
            MethodDeclarationSyntax methodDeclaration => methodDeclaration.Body,
            LocalFunctionStatementSyntax localFunction => localFunction.Body,
            _ => null
        };

    private static ExpressionSyntax? SingleReturnExpression(BlockSyntax body)
    {
        var returns = body.DescendantNodes(static node =>
                node is not LambdaExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<ReturnStatementSyntax>()
            .ToArray();
        return returns.Length == 1 ? returns[0].Expression : null;
    }

    private static bool IsGeneratedRemoteChain(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var seed = WalkToSeed(expression, model, cancellationToken);
        var seedModel = seed is null ? null : HookChainSemanticModelResolver.For(seed, model);
        return seed is not null &&
            seedModel is not null &&
            GeneratedRemoteHookChainFallback.Candidate(seed, seedModel, cancellationToken) is not null;
    }

    private static InvocationExpressionSyntax? WalkToSeed(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var current = expression;
        var currentModel = model;
        while (true)
        {
            if (!TryResolveSeedExpression(current, currentModel, cancellationToken, out current, out currentModel))
            {
                return null;
            }

            if (TrySeedInvocation(current, out var seed))
            {
                return seed;
            }

            if (!TryStageReceiver(current, out current))
            {
                return null;
            }
        }
    }

    private static bool TryResolveSeedExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ExpressionSyntax resolved,
        out SemanticModel resolvedModel)
    {
        resolved = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        var semanticModel = HookChainSemanticModelResolver.For(resolved, model);
        if (semanticModel is null)
        {
            resolvedModel = null!;
            return false;
        }

        resolvedModel = semanticModel;
        while (HookChainAliasResolver.Initializer(resolved, resolvedModel, cancellationToken) is { } initializer)
        {
            resolved = HookChainAliasResolver.UnwrapTransparentExpression(initializer);
            semanticModel = HookChainSemanticModelResolver.For(resolved, resolvedModel);
            if (semanticModel is null)
            {
                resolvedModel = null!;
                return false;
            }

            resolvedModel = semanticModel;
        }

        return true;
    }

    private static bool TrySeedInvocation(ExpressionSyntax expression, out InvocationExpressionSyntax invocation)
    {
        if (expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax access
            } candidate &&
            string.Equals(access.Name.Identifier.ValueText, "On", StringComparison.Ordinal))
        {
            invocation = candidate;
            return true;
        }

        invocation = null!;
        return false;
    }

    private static bool TryStageReceiver(ExpressionSyntax expression, out ExpressionSyntax receiver)
    {
        if (expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax access
            })
        {
            var name = access.Name.Identifier.ValueText;
            if (name is "Where" or "Select")
            {
                receiver = access.Expression;
                return true;
            }
        }

        receiver = null!;
        return false;
    }

    private static bool IsRemoteChainOrStageType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var name = named.Name;
        var ns = named.ContainingNamespace.ToDisplayString();
        return ns switch
        {
            "DotBoxD.Plugins.Runtime" => IsRemotePipelineTypeName(name),
            "DotBoxD.Plugins.Runtime.Hooks" => IsRemoteHookStageTypeName(name),
            "DotBoxD.Plugins.Runtime.Subscriptions" => IsRemoteSubscriptionStageTypeName(name),
            _ => false
        };
    }

    private static bool IsRemotePipelineTypeName(string name)
        => name is "RemoteHookPipeline" or
            "RemoteHookPipelineWithContext" or
            "RemoteSubscriptionPipeline" or
            "RemoteSubscriptionPipelineWithContext";

    private static bool IsRemoteHookStageTypeName(string name)
        => name is "RemoteHookStage" or "RemoteHookStageWithContext";

    private static bool IsRemoteSubscriptionStageTypeName(string name)
        => name is "RemoteSubscriptionStage" or "RemoteSubscriptionStageWithContext";

    private static bool IsRemoteStageType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var ns = named.ContainingNamespace.ToDisplayString();
        return ns switch
        {
            "DotBoxD.Plugins.Runtime.Hooks" => named.Name is "RemoteHookStage" or "RemoteHookStageWithContext",
            "DotBoxD.Plugins.Runtime.Subscriptions" => named.Name is
                "RemoteSubscriptionStage" or
                "RemoteSubscriptionStageWithContext",
            _ => false
        };
    }
}

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

        if (ContainsStageInvocation(expression))
        {
            return true;
        }

        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        if (expression is ConditionalExpressionSyntax conditional)
        {
            return ContainsStageInvocationOrAlias(conditional.WhenTrue, model, cancellationToken, depth + 1) ||
                ContainsStageInvocationOrAlias(conditional.WhenFalse, model, cancellationToken, depth + 1);
        }

        if (ReturnedExpression(expression, model, cancellationToken) is { } returned)
        {
            return ContainsStageInvocationOrAlias(returned, model, cancellationToken, depth + 1);
        }

        return HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer &&
            ContainsStageInvocationOrAlias(initializer, model, cancellationToken, depth + 1);
    }

    private static ExpressionSyntax? ReturnedExpression(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);
        if (InvokedMethod(expression, model, cancellationToken) is not { } method)
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

        return model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
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
        return seed is not null &&
            GeneratedRemoteHookChainFallback.Candidate(seed, model, cancellationToken) is not null;
    }

    private static InvocationExpressionSyntax? WalkToSeed(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        expression = HookChainAliasResolver.UnwrapTransparentExpression(expression);

        if (HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer)
        {
            return WalkToSeed(initializer, model, cancellationToken);
        }

        var current = expression;
        while (true)
        {
            current = HookChainAliasResolver.UnwrapTransparentExpression(current);
            if (HookChainAliasResolver.Initializer(current, model, cancellationToken) is { } currentInitializer)
            {
                current = currentInitializer;
                continue;
            }

            if (current is not InvocationExpressionSyntax invocation ||
                invocation.Expression is not MemberAccessExpressionSyntax access)
            {
                return null;
            }

            var name = access.Name.Identifier.ValueText;
            if (string.Equals(name, "On", StringComparison.Ordinal))
            {
                return invocation;
            }

            if (name is "Where" or "Select")
            {
                current = access.Expression;
                continue;
            }

            return null;
        }
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

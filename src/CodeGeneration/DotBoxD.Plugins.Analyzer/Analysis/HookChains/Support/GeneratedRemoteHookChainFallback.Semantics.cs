using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    public static PipelineStepRole? RoleOfUnresolvedGeneratedSurface(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        IMethodSymbol? resolvedMethod)
    {
        if (resolvedMethod is { ContainingType.TypeKind: not TypeKind.Error })
        {
            return null;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax access ||
            GeneratedRemoteRole(access.Name.Identifier.ValueText) is not { } role)
        {
            return null;
        }

        if (role == PipelineStepRole.Seed)
        {
            return Candidate(invocation, model, cancellationToken) is null
                ? null
                : PipelineStepRole.Seed;
        }

        return HasGeneratedRemoteSeed(access.Expression, model, cancellationToken, depth: 0)
            ? role
            : null;
    }

    private static PipelineStepRole? GeneratedRemoteRole(string methodName)
        => methodName switch
        {
            "On" => PipelineStepRole.Seed,
            "Where" => PipelineStepRole.Filter,
            "Select" => PipelineStepRole.Projection,
            "Run" => PipelineStepRole.Run,
            "RunLocal" => PipelineStepRole.RunLocal,
            "Register" => PipelineStepRole.Register,
            "RegisterLocal" => PipelineStepRole.RegisterLocal,
            _ => null,
        };

    private static bool HasGeneratedRemoteSeed(
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
        if (HookChainAliasResolver.Initializer(expression, model, cancellationToken) is { } initializer)
        {
            return HasGeneratedRemoteSeed(initializer, model, cancellationToken, depth + 1);
        }

        if (expression is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax access ||
            GeneratedRemoteRole(access.Name.Identifier.ValueText) is not { } role)
        {
            return false;
        }

        return role == PipelineStepRole.Seed
            ? Candidate(invocation, model, cancellationToken) is not null
            : HasGeneratedRemoteSeed(access.Expression, model, cancellationToken, depth + 1);
    }

    private static ITypeSymbol? TypeFromSyntax(
        TypeSyntax typeSyntax,
        SemanticModel model,
        CancellationToken cancellationToken)
        => SemanticModelFor(typeSyntax, model)?.GetTypeInfo(typeSyntax, cancellationToken).Type;

    private static SemanticModel? SemanticModelFor(SyntaxNode node, SemanticModel model)
    {
        if (ReferenceEquals(node.SyntaxTree, model.SyntaxTree))
        {
            return model;
        }

        foreach (var tree in model.Compilation.SyntaxTrees)
        {
            if (ReferenceEquals(tree, node.SyntaxTree))
            {
                return model.Compilation.GetSemanticModel(tree);
            }
        }

        return null;
    }
}

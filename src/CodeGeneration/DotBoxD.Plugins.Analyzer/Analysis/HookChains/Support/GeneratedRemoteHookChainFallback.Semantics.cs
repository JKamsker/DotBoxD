using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    public static PipelineCallRole? RoleOfUnresolvedGeneratedSurface(
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

        if (role == PipelineCallRole.Seed)
        {
            return Candidate(invocation, model, cancellationToken) is null
                ? null
                : PipelineCallRole.Seed;
        }

        return HasGeneratedRemoteSeed(access.Expression, model, cancellationToken, depth: 0)
            ? role
            : null;
    }

    private static PipelineCallRole? GeneratedRemoteRole(string methodName)
        => methodName switch
        {
            "On" => PipelineCallRole.Seed,
            "Where" => PipelineCallRole.Filter,
            "Select" => PipelineCallRole.Projection,
            "Run" => PipelineCallRole.Run,
            "RunLocal" => PipelineCallRole.RunLocal,
            "Register" => PipelineCallRole.Register,
            "RegisterLocal" => PipelineCallRole.RegisterLocal,
            _ => null,
        };

    private static bool HasGeneratedRemoteSeed(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (depth > HookChainAliasResolver.MaxResolutionDepth)
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

        return role == PipelineCallRole.Seed
            ? Candidate(invocation, model, cancellationToken) is not null
            : HasGeneratedRemoteSeed(access.Expression, model, cancellationToken, depth + 1);
    }

    private static ITypeSymbol? TypeFromSyntax(
        TypeSyntax typeSyntax,
        SemanticModel model,
        CancellationToken cancellationToken)
        => HookChainSemanticModelResolver.For(typeSyntax, model)?.GetTypeInfo(typeSyntax, cancellationToken).Type;
}

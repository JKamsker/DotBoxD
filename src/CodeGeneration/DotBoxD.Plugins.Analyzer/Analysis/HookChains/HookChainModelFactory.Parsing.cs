using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    // Resolves the pipeline role of a resolved method from a [PipelineSurface] receiver and explicit
    // delegate+IR method shape. Public per-method role markers no longer exist.
    private static PipelineCallRole? RoleOf(IMethodSymbol? method, Compilation compilation)
        => PipelineRoleReader.RoleOf(method, compilation);

    private static PipelineCallRole? RoleOf(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(invocation, cancellationToken);
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
        if (RoleOf(symbol as IMethodSymbol, model.Compilation) is { } role)
        {
            return role;
        }

        return GeneratedRemoteHookChainFallback.RoleOfUnresolvedGeneratedSurface(
            invocation,
            model,
            cancellationToken,
            symbol as IMethodSymbol);
    }

    private static InvocationExpressionSyntax? WalkToSeed(
        ExpressionSyntax receiver,
        List<HookChainStage> stages,
        SemanticModel model,
        CancellationToken cancellationToken)
        => WalkToSeed(receiver, stages, model, cancellationToken, depth: 0);

    private static InvocationExpressionSyntax? WalkToSeed(
        ExpressionSyntax receiver,
        List<HookChainStage> stages,
        SemanticModel model,
        CancellationToken cancellationToken,
        int depth)
    {
        if (depth > 8)
        {
            return null;
        }

        receiver = HookChainAliasResolver.UnwrapTransparentExpression(receiver);
        if (HookChainAliasResolver.Initializer(receiver, model, cancellationToken) is { } initializer)
        {
            return WalkToSeed(initializer, stages, model, cancellationToken, depth + 1);
        }

        var current = receiver;
        while (TryWalkStage(current, stages, model, cancellationToken, out var seed, out var next))
        {
            if (seed is not null)
            {
                return seed;
            }

            current = next!;
        }

        return null;
    }

    private static bool TryWalkStage(
        ExpressionSyntax current,
        List<HookChainStage> stages,
        SemanticModel model,
        CancellationToken cancellationToken,
        out InvocationExpressionSyntax? seed,
        out ExpressionSyntax? next)
    {
        seed = null;
        next = null;
        current = HookChainAliasResolver.UnwrapTransparentExpression(current);
        if (HookChainAliasResolver.Initializer(current, model, cancellationToken) is { } currentInitializer)
        {
            next = currentInitializer;
            return true;
        }

        if (current is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return false;
        }

        var role = RoleOf(invocation, model, cancellationToken);
        if (role == PipelineCallRole.Seed)
        {
            seed = invocation;
            return true;
        }

        if (role is not (PipelineCallRole.Filter or PipelineCallRole.Projection))
        {
            return false;
        }

        if (!TryLambda(invocation, out var lambda))
        {
            return false;
        }

        if (IsResolvedNonDotBoxDStageMethodInvocation(invocation, model, cancellationToken))
        {
            return false;
        }

        stages.Add(new HookChainStage(role == PipelineCallRole.Projection, invocation, lambda));
        next = HookChainAliasResolver.UnwrapTransparentExpression(access.Expression);
        return true;
    }

    private static bool IsResolvedNonDotBoxDStageMethodInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
        => model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method &&
           (method.ContainingType is null || ReceiverKind(method.ContainingType, model.Compilation) is null);

    internal static HookChainReceiverKind? ReceiverKind(
        SemanticModel model,
        ExpressionSyntax receiver,
        CancellationToken cancellationToken)
    {
        if (model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol type)
        {
            return null;
        }

        return ReceiverKind(type, model.Compilation);
    }

    internal static HookChainReceiverKind? ReceiverKind(INamedTypeSymbol type, Compilation compilation)
        => PipelineRoleReader.Transport(type, compilation);

    // Accepts both lambda forms a fluent stage can take: a parenthesized lambda (e), (e, ctx) or (),
    // and the simple form e => .... A stage may also spell its optional IRFunc companion argument explicitly;
    // the lambda's arity is resolved later by LambdaParameters.
    private static bool TryLambda(InvocationExpressionSyntax invocation, out LambdaExpressionSyntax lambda)
    {
        lambda = null!;
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is LambdaExpressionSyntax lambdaExpression)
            {
                lambda = lambdaExpression;
                return true;
            }
        }

        return false;
    }

    // The handler lambda of a result terminal - Register(lambda, priority) / RegisterLocal(lambda, priority) -
    // allowing a named/reordered handler argument such as Register(priority: 10, handler: ctx => ...).
    private static bool TryLeadingLambda(InvocationExpressionSyntax invocation, out LambdaExpressionSyntax lambda)
    {
        lambda = null!;
        var arguments = invocation.ArgumentList.Arguments;
        LambdaExpressionSyntax? firstUnnamedLambda = null;
        foreach (var argument in arguments)
        {
            if (argument.NameColon is { Name.Identifier.ValueText: "handler" })
            {
                if (argument.Expression is not LambdaExpressionSyntax namedHandler)
                {
                    return false;
                }

                lambda = namedHandler;
                return true;
            }

            if (argument.NameColon is null &&
                firstUnnamedLambda is null &&
                argument.Expression is LambdaExpressionSyntax unnamedHandler)
            {
                firstUnnamedLambda = unnamedHandler;
            }
        }

        if (firstUnnamedLambda is null)
        {
            return false;
        }

        lambda = firstUnnamedLambda;
        return true;
    }

    // Element-only lambdas (e =>, (e) =>) yield (element, null); element+context lambdas ((e, ctx) =>)
    // yield (element, context). Other arities are unsupported.
    private static (string? ElementParam, string? ContextParam) LambdaParameters(
        LambdaExpressionSyntax lambda)
    {
        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simple:
                return (simple.Parameter.Identifier.ValueText, null);
            case ParenthesizedLambdaExpressionSyntax parenthesized:
                var parameters = parenthesized.ParameterList.Parameters;
                return parameters.Count switch
                {
                    1 => (parameters[0].Identifier.ValueText, null),
                    2 => (parameters[0].Identifier.ValueText, parameters[1].Identifier.ValueText),
                    _ => (null, null),
                };
            default:
                return (null, null);
        }
    }

    private static bool ContainsUnsupported(EquatableArray<EventPropertyModel> eventProperties)
    {
        for (var i = 0; i < eventProperties.Count; i++)
        {
            if (eventProperties[i].Type == DotBoxDGenerationNames.ManifestTypes.Unsupported)
            {
                return true;
            }
        }

        return false;
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class UnsupportedStageDiagnosticAnalyzer
{
    internal static bool TryCreate(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PluginKernelDiagnostic diagnostic)
    {
        diagnostic = null!;
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess ||
            !IsTerminal(invocation, model, cancellationToken))
        {
            return false;
        }

        return TryFindUnsupportedStage(terminalAccess.Expression, model, cancellationToken, out diagnostic);
    }

    private static bool TryFindUnsupportedStage(
        ExpressionSyntax receiver,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PluginKernelDiagnostic diagnostic)
    {
        diagnostic = null!;
        foreach (var stage in StageInvocations(receiver))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryUnsupportedStageLocation(stage, model, cancellationToken, out var location))
            {
                continue;
            }

            diagnostic = new PluginKernelDiagnostic(
                "Hook chain Where/Select stages do not support anonymous method delegate syntax; use a lambda expression or hand-write the equivalent public IR package.",
                location);
            return true;
        }

        return false;
    }

    private static bool IsTerminal(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
        => RoleOf(invocation, model, cancellationToken) is
            PipelineCallRole.Run or PipelineCallRole.RunLocal or PipelineCallRole.Register or
            PipelineCallRole.RegisterLocal;

    private static bool TryUnsupportedStageLocation(
        InvocationExpressionSyntax stage,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PluginDiagnosticLocation location)
    {
        location = default;
        if (stage.Expression is not MemberAccessExpressionSyntax stageAccess ||
            RoleOf(stage, model, cancellationToken) is not (PipelineCallRole.Filter or PipelineCallRole.Projection) ||
            !HasAnonymousMethodArgument(stage))
        {
            return false;
        }

        location = PluginDiagnosticLocation.From(stageAccess.Name.GetLocation());
        return true;
    }

    private static IEnumerable<InvocationExpressionSyntax> StageInvocations(ExpressionSyntax expression)
    {
        var current = expression;
        while (true)
        {
            current = HookChainAliasResolver.UnwrapTransparentExpression(current);
            if (current is InvocationExpressionSyntax invocation)
            {
                yield return invocation;
                current = invocation.Expression;
                continue;
            }

            if (current is MemberAccessExpressionSyntax memberAccess)
            {
                current = memberAccess.Expression;
                continue;
            }

            break;
        }
    }

    private static bool HasAnonymousMethodArgument(InvocationExpressionSyntax invocation)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is AnonymousMethodExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static PipelineCallRole? RoleOf(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(invocation, cancellationToken);
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
        if (PipelineRoleReader.RoleOf(symbol as IMethodSymbol, model.Compilation) is { } role)
        {
            return role;
        }

        return GeneratedRemoteHookChainFallback.RoleOfUnresolvedGeneratedSurface(
            invocation,
            model,
            cancellationToken,
            symbol as IMethodSymbol);
    }
}

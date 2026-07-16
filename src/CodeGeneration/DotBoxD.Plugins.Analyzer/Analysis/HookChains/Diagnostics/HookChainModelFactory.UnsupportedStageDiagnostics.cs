using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static bool TryUnsupportedAnonymousMethodStageDiagnostic(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PluginKernelDiagnostic diagnostic)
    {
        diagnostic = null!;
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess ||
            RoleOf(invocation, model, cancellationToken) is not
                (PipelineCallRole.Run or PipelineCallRole.RunLocal or PipelineCallRole.Register or
                    PipelineCallRole.RegisterLocal))
        {
            return false;
        }

        foreach (var stage in StageInvocations(terminalAccess.Expression))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stage.Expression is not MemberAccessExpressionSyntax stageAccess ||
                RoleOf(stage, model, cancellationToken) is not (PipelineCallRole.Filter or PipelineCallRole.Projection) ||
                !HasAnonymousMethodArgument(stage))
            {
                continue;
            }

            diagnostic = new PluginKernelDiagnostic(
                "Hook chain Where/Select stages do not support anonymous method delegate syntax; use a lambda expression or hand-write the equivalent public IR package.",
                PluginDiagnosticLocation.From(stageAccess.Name.GetLocation()));
            return true;
        }

        return false;
    }

    private static IEnumerable<InvocationExpressionSyntax> StageInvocations(ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax invocation)
        {
            yield return invocation;
        }

        foreach (var nested in expression.DescendantNodes(static node =>
                node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<InvocationExpressionSyntax>())
        {
            yield return nested;
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
}

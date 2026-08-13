using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class ConditionalHookChainDiagnosticFactory
{
    private const string Message =
        "Hook chain terminal '{0}' cannot be invoked through conditional access; call the terminal directly on a non-null chain so the source generator can intercept it.";

    public static bool IsCandidate(SyntaxNode node)
        => node is ConditionalAccessExpressionSyntax
        {
            WhenNotNull: InvocationExpressionSyntax
            {
                Expression: MemberBindingExpressionSyntax
                {
                    Name.Identifier.ValueText: "Run" or "RunLocal" or "Register" or "RegisterLocal" or
                        "Use" or "UseGeneratedChain" or "UseGeneratedLocalChain" or
                        "UseGeneratedResultChain" or "UseGeneratedLocalResultChain"
                }
            }
        };

    public static PluginKernelDiagnostic? Create(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var conditional = (ConditionalAccessExpressionSyntax)context.Node;
        if (conditional.WhenNotNull is not InvocationExpressionSyntax
            {
                Expression: MemberBindingExpressionSyntax binding
            } invocation)
        {
            return null;
        }

        if (context.SemanticModel.GetTypeInfo(conditional.Expression, cancellationToken).Type
                is not INamedTypeSymbol receiverType ||
            HookChainModelFactory.ReceiverKind(receiverType, context.SemanticModel.Compilation) is not { } receiverKind ||
            !IsHookChainTerminal(invocation, receiverKind, context.SemanticModel, cancellationToken))
        {
            return null;
        }

        return new PluginKernelDiagnostic(
            string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                Message,
                binding.Name.Identifier.ValueText),
            PluginDiagnosticLocation.From(binding.Name.GetLocation()));
    }

    private static bool IsHookChainTerminal(
        InvocationExpressionSyntax invocation,
        HookChainReceiverKind receiverKind,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var method = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        if (PipelineRoleReader.RoleOf(method, model.Compilation) is { } role &&
            HookChainInstallKindResolver.Resolve(role, receiverKind, generatedRemoteKind: null) is { } installKind)
        {
            return HookChainModelFactory.HasTerminalShape(invocation, installKind, model, cancellationToken);
        }

        return method?.ContainingType is { } containingType &&
            PipelineRoleReader.Transport(containingType, model.Compilation) is not null &&
            IsGeneratedTerminal(invocation, method.Name, model, cancellationToken);
    }

    private static bool IsGeneratedTerminal(
        InvocationExpressionSyntax invocation,
        string methodName,
        SemanticModel model,
        CancellationToken cancellationToken)
        => methodName switch
        {
            "Use" or "UseGeneratedChain" or "UseGeneratedResultChain" => true,
            "UseGeneratedLocalChain" => HookChainModelFactory.HasTerminalShape(
                invocation,
                HookChainInterceptorInstallKind.LocalCallback,
                model,
                cancellationToken),
            "UseGeneratedLocalResultChain" => HookChainModelFactory.HasTerminalShape(
                invocation,
                HookChainInterceptorInstallKind.LocalResultChain,
                model,
                cancellationToken),
            _ => false,
        };
}

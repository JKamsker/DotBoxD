using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotBoxD.Plugins.Analyzer.Analysis;

public sealed partial class PluginAnalyzer
{
    private static void ValidateLocalMember(
        SymbolAnalysisContext context,
        ISymbol member,
        IMethodSymbol method)
    {
        ValidateLocalMemberCore(context, member, method.IsStatic);
    }

    private static void ValidateLocalMember(
        SymbolAnalysisContext context,
        ISymbol member,
        IPropertySymbol property)
    {
        ValidateLocalMemberCore(context, member, property.IsStatic);
    }

    private static void ValidateLocalMemberCore(SymbolAnalysisContext context, ISymbol member, bool isStatic)
    {
        if (isStatic || !IsDeclaredPluginServerContext(context.Compilation, member.ContainingType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PluginAnalyzerDiagnostics.LocalContextMemberRule,
                member.Locations.FirstOrDefault(),
                "[NativeOnly] is valid only on instance members of the declared generated plugin server context."));
        }
    }

    private static bool IsDeclaredPluginServerContext(Compilation compilation, INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        foreach (var symbol in compilation.GetSymbolsWithName(static _ => true, SymbolFilter.Type))
        {
            if (symbol is not INamedTypeSymbol server)
            {
                continue;
            }

            if (ServerDeclaresContext(server, type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ServerDeclaresContext(INamedTypeSymbol server, INamedTypeSymbol type)
    {
        foreach (var attribute in server.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.GeneratePluginServerAttribute,
                    StringComparison.Ordinal) &&
                attribute.NamedArguments.Any(argument =>
                    string.Equals(argument.Key, "Context", StringComparison.Ordinal) &&
                    argument.Value.Value is INamedTypeSymbol contextType &&
                    SymbolEqualityComparer.Default.Equals(contextType, type)))
            {
                return true;
            }
        }

        return false;
    }

    private static void ReportLocalUseIfInvalid(OperationAnalysisContext context, ISymbol target)
    {
        var model = context.Operation.SemanticModel;
        if (!HasAttribute(target, DotBoxDMetadataNames.NativeOnlyAttribute) ||
            model is null ||
            !IsLocalUseForbidden(context.Operation.Syntax, context.ContainingSymbol, model, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            PluginAnalyzerDiagnostics.LocalContextMemberRule,
            context.Operation.Syntax.GetLocation(),
            "[NativeOnly] context members run natively and cannot be used in lowered hook chains or server-extension bodies."));
    }

    private static bool IsLocalUseForbidden(
        SyntaxNode syntax,
        ISymbol? containingSymbol,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (IsServerExtensionMethod(containingSymbol))
        {
            return true;
        }

        foreach (var lambda in syntax.AncestorsAndSelf().OfType<LambdaExpressionSyntax>())
        {
            if (IsForbiddenHookChainLambda(lambda, model, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsServerExtensionMethod(ISymbol? symbol)
        => symbol is IMethodSymbol method &&
           HasAttribute(method, DotBoxDMetadataNames.ServerExtensionMethodAttribute);

    private static bool IsForbiddenHookChainLambda(
        LambdaExpressionSyntax lambda,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (lambda.Parent is not ArgumentSyntax argument ||
            argument.Parent is not ArgumentListSyntax argumentList ||
            argumentList.Parent is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return false;
        }

        return IsLoweredPipelineStep(invocation, member.Expression, model, cancellationToken);
    }

    private static bool IsHookChainReceiver(
        ExpressionSyntax receiver,
        SemanticModel model,
        CancellationToken cancellationToken)
        => model.GetTypeInfo(receiver, cancellationToken).Type is INamedTypeSymbol receiverType &&
           PipelineRoleReader.Transport(receiverType, model.Compilation) is not null;

    private static bool IsLoweredPipelineStep(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax receiver,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var info = model.GetSymbolInfo(invocation, cancellationToken);
        var method = info.Symbol as IMethodSymbol ??
            (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] as IMethodSymbol : null);
        if (IsForbiddenLocalUseRole(PipelineRoleReader.RoleOf(method, model.Compilation)))
        {
            return IsHookChainReceiver(receiver, model, cancellationToken);
        }

        return IsForbiddenLocalUseRole(GeneratedRemoteHookChainFallback.RoleOfUnresolvedGeneratedSurface(
            invocation,
            model,
            cancellationToken,
            method));
    }

    private static bool IsForbiddenLocalUseRole(PipelineCallRole? role)
        => role is
            PipelineCallRole.Filter or
            PipelineCallRole.Projection or
            PipelineCallRole.Run or
            PipelineCallRole.Register;
}

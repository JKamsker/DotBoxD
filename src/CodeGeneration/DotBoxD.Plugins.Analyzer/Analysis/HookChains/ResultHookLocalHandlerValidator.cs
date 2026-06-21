using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class ResultHookLocalHandlerValidator
{
    public static void EnsureReturnsHookResult(
        LambdaExpressionSyntax terminalLambda,
        INamedTypeSymbol resultType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetTypeInfo(terminalLambda, cancellationToken).ConvertedType is INamedTypeSymbol delegateType &&
            delegateType.DelegateInvokeMethod?.ReturnType is { } returnType &&
            ReturnsHookResult(returnType, resultType))
        {
            return;
        }

        if (terminalLambda.ExpressionBody is InvocationExpressionSyntax builderChain &&
            SymbolEqualityComparer.Default.Equals(
                DotBoxDResultBuilderExpressionLowerer.ResolveSeedResultType(builderChain, model, cancellationToken),
                resultType))
        {
            return;
        }

        if (terminalLambda.ExpressionBody is ObjectCreationExpressionSyntax creation &&
            SymbolEqualityComparer.Default.Equals(
                model.GetTypeInfo(creation, cancellationToken).ConvertedType ??
                model.GetTypeInfo(creation, cancellationToken).Type,
                resultType))
        {
            return;
        }

        throw new NotSupportedException();
    }

    private static bool ReturnsHookResult(ITypeSymbol returnType, INamedTypeSymbol resultType)
    {
        if (SymbolEqualityComparer.Default.Equals(returnType, resultType))
        {
            return true;
        }

        return returnType is INamedTypeSymbol
        {
            Name: "ValueTask",
            IsGenericType: true,
            TypeArguments.Length: 1,
            ContainingNamespace: { } ns
        } valueTask &&
        string.Equals(ns.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal) &&
        SymbolEqualityComparer.Default.Equals(valueTask.TypeArguments[0], resultType);
    }
}

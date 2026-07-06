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

        var returnExpressions = ReturnExpressions(terminalLambda).ToArray();
        if (returnExpressions.Length > 0 &&
            returnExpressions.All(returnExpression =>
                ReturnsHookResultExpression(returnExpression, resultType, model, cancellationToken)))
        {
            return;
        }

        throw new NotSupportedException();
    }

    private static bool ReturnsHookResultExpression(
        ExpressionSyntax returnExpression,
        INamedTypeSymbol resultType,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (returnExpression is InvocationExpressionSyntax builderChain &&
            SymbolEqualityComparer.Default.Equals(
                DotBoxDResultBuilderExpressionLowerer.ResolveSeedResultType(builderChain, model, cancellationToken),
                resultType))
        {
            return true;
        }

        if (returnExpression is ObjectCreationExpressionSyntax creation)
        {
            var typeInfo = model.GetTypeInfo(creation, cancellationToken);
            return SymbolEqualityComparer.Default.Equals(typeInfo.ConvertedType ?? typeInfo.Type, resultType);
        }

        return false;
    }

    private static IEnumerable<ExpressionSyntax> ReturnExpressions(LambdaExpressionSyntax lambda)
    {
        if (lambda.ExpressionBody is { } expressionBody)
        {
            yield return expressionBody;
            yield break;
        }

        if (lambda.Block is null)
        {
            yield break;
        }

        foreach (var statement in lambda.Block.DescendantNodes(static node =>
            node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax))
        {
            if (statement is ReturnStatementSyntax { Expression: { } expression })
            {
                yield return expression;
            }
        }
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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcDtoConstructorLocalAliasInspector
{
    public static bool PreservesParameter(
        ILocalSymbol local,
        ConstructorDeclarationSyntax declaration,
        SemanticModel model,
        Func<ExpressionSyntax, bool> preservesParameter)
    {
        if (local.DeclaringSyntaxReferences.Length != 1 ||
            local.DeclaringSyntaxReferences[0].GetSyntax() is not VariableDeclaratorSyntax declarator ||
            declarator.Initializer is not { Value: { } initializer } ||
            IsAssigned(declaration, local, model))
        {
            return false;
        }

        return preservesParameter(initializer);
    }

    private static bool IsAssigned(
        ConstructorDeclarationSyntax declaration,
        ILocalSymbol local,
        SemanticModel model)
        => declaration.DescendantNodes().Any(node =>
            WrittenSymbol(node, model) is { } symbol &&
            SymbolEqualityComparer.Default.Equals(symbol, local));

    private static ISymbol? WrittenSymbol(SyntaxNode node, SemanticModel model)
        => node switch
        {
            AssignmentExpressionSyntax assignment => model.GetSymbolInfo(assignment.Left).Symbol,
            PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.PreIncrementExpression) ||
                                                  unary.IsKind(SyntaxKind.PreDecrementExpression) =>
                model.GetSymbolInfo(unary.Operand).Symbol,
            PostfixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.PostIncrementExpression) ||
                                                   unary.IsKind(SyntaxKind.PostDecrementExpression) =>
                model.GetSymbolInfo(unary.Operand).Symbol,
            ArgumentSyntax { RefKindKeyword.RawKind: not 0, Expression: var written } =>
                model.GetSymbolInfo(written).Symbol,
            _ => null,
        };
}

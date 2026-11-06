using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

/// <summary>
/// Identifies [KernelMethod] argument operators whose C# evaluation can throw (or write), so the argument is not
/// "repeatable": inlining may drop an unused parameter or evaluate a reused one twice, and dropping
/// <c>100 / e.Count</c> would silently lose C#'s DivideByZeroException. Integer <c>/</c> and <c>%</c> always
/// trap; <c>+ - *</c> and unary <c>-</c> trap only in a checked context.
/// </summary>
internal static class KernelMethodTrappingOperators
{
    public static bool CanTrap(BinaryExpressionSyntax binary, SemanticModel? model, CancellationToken cancellationToken)
    {
        switch (binary.Kind())
        {
            case SyntaxKind.DivideExpression:
            case SyntaxKind.ModuloExpression:
                return !IsFloatingPoint(binary, model, cancellationToken);
            case SyntaxKind.AddExpression:
            case SyntaxKind.SubtractExpression:
            case SyntaxKind.MultiplyExpression:
                return model is null
                    ? IsInsideCheckedSyntax(binary)
                    : model.GetOperation(binary, cancellationToken) is IBinaryOperation { IsChecked: true };
            default:
                return false;
        }
    }

    public static bool CanTrapOrMutate(PrefixUnaryExpressionSyntax unary, SemanticModel? model, CancellationToken cancellationToken)
        => unary.Kind() switch
        {
            SyntaxKind.UnaryPlusExpression or SyntaxKind.LogicalNotExpression or SyntaxKind.BitwiseNotExpression => false,
            SyntaxKind.UnaryMinusExpression => model is null
                ? IsInsideCheckedSyntax(unary)
                : model.GetOperation(unary, cancellationToken) is IUnaryOperation { IsChecked: true },
            // ++/--, address-of, dereference and the rest write or touch memory: never repeatable.
            _ => true
        };

    private static bool IsFloatingPoint(BinaryExpressionSyntax binary, SemanticModel? model, CancellationToken cancellationToken)
        => model?.GetTypeInfo(binary, cancellationToken).Type?.SpecialType is SpecialType.System_Double or SpecialType.System_Single;

    // Without a semantic model the compilation-wide CheckOverflow option is unknown; honor explicit checked syntax.
    private static bool IsInsideCheckedSyntax(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case CheckedExpressionSyntax expression:
                    return expression.IsKind(SyntaxKind.CheckedExpression);
                case CheckedStatementSyntax statement:
                    return statement.IsKind(SyntaxKind.CheckedStatement);
            }
        }

        return false;
    }
}

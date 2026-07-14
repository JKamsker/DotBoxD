using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncModelFactory
{
    private static bool IsUnqualifiedInvocationExpression(ExpressionSyntax expression)
        => expression is IdentifierNameSyntax or GenericNameSyntax;

    private static bool IsConditionalInvocationExpression(ExpressionSyntax expression)
        => expression is MemberBindingExpressionSyntax;
}

internal static class InvokeAsyncArgumentSyntax
{
    public static bool IsNullLike(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;
                case CastExpressionSyntax cast when IsBuiltInConversion(cast, model, cancellationToken):
                    expression = cast.Expression;
                    break;
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    break;
                default:
                    return expression.IsKind(SyntaxKind.NullLiteralExpression) ||
                           expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
                           expression.IsKind(SyntaxKind.DefaultExpression);
            }
        }
    }

    private static bool IsBuiltInConversion(
        CastExpressionSyntax cast,
        SemanticModel model,
        CancellationToken cancellationToken)
        => model.GetOperation(cast, cancellationToken) is IConversionOperation { OperatorMethod: null };
}

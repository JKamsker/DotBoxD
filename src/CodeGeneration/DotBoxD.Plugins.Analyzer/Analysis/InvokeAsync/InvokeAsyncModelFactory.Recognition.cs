using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    public static bool IsNullLike(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;
                case CastExpressionSyntax cast:
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
}

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
                case BinaryExpressionSyntax conversion
                    when conversion.IsKind(SyntaxKind.AsExpression) &&
                         IsBuiltInConversion(conversion, model, cancellationToken):
                    expression = conversion.Left;
                    break;
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    break;
                default:
                    var constant = model.GetConstantValue(expression, cancellationToken);
                    return constant.HasValue && constant.Value is null;
            }
        }
    }

    private static bool IsBuiltInConversion(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
        => model.GetOperation(expression, cancellationToken) is IConversionOperation { OperatorMethod: null };
}

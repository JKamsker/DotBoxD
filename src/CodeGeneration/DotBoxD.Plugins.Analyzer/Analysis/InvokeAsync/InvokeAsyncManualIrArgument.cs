using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncManualIrArgument
{
    public static bool IsExplicit(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (model.GetOperation(invocation, cancellationToken) is IInvocationOperation operation)
        {
            return HasExplicitBoundArgument(operation);
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon is { Name.Identifier.ValueText: "irInvocation" })
            {
                return !IsNullLike(argument.Expression);
            }
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var type = model.GetTypeInfo(argument.Expression, cancellationToken);
            if ((type.Type is not null && InvokeAsyncServerSurface.IsIRInvocation(type.Type)) ||
                (type.ConvertedType is not null && InvokeAsyncServerSurface.IsIRInvocation(type.ConvertedType)))
            {
                return !IsNullLike(argument.Expression);
            }
        }

        return false;
    }

    private static bool HasExplicitBoundArgument(IInvocationOperation operation)
    {
        foreach (var argument in operation.Arguments)
        {
            if (argument.IsImplicit ||
                argument.Parameter is null ||
                !InvokeAsyncServerSurface.IsIRInvocation(argument.Parameter.Type))
            {
                continue;
            }

            var expression = argument.Syntax is ArgumentSyntax syntax
                ? syntax.Expression
                : argument.Value.Syntax as ExpressionSyntax;
            return expression is not null && !IsNullLike(expression);
        }

        return false;
    }

    private static bool IsNullLike(ExpressionSyntax expression)
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

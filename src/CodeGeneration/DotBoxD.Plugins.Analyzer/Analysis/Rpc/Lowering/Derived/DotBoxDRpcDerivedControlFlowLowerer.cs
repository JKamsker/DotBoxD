using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed class DotBoxDRpcDerivedControlFlowLowerer(
    Func<ExpressionSyntax, string> lowerExpression,
    Func<string, string> reserveGeneratedLocal,
    Action<string> addExpressionPrelude,
    Func<NotSupportedException> unsupported)
{
    public string? TryLower(ExpressionSyntax expression)
        => TryLowerConditional(expression) ?? TryLowerSwitch(expression);

    private string? TryLowerConditional(ExpressionSyntax expression)
    {
        if (expression is not ConditionalExpressionSyntax conditional)
        {
            return null;
        }

        var localName = reserveGeneratedLocal("__sir_derived");
        addExpressionPrelude(DotBoxDRpcJsonLowerer.Obj(
            ("op", DotBoxDRpcJsonLowerer.Str("if")),
            ("condition", lowerExpression(conditional.Condition)),
            ("then", "[" + DotBoxDRpcJsonLowerer.SetStatement(
                localName,
                lowerExpression(conditional.WhenTrue)) + "]"),
            ("else", "[" + DotBoxDRpcJsonLowerer.SetStatement(
                localName,
                lowerExpression(conditional.WhenFalse)) + "]")));
        return DotBoxDRpcJsonLowerer.Var(localName);
    }

    private string? TryLowerSwitch(ExpressionSyntax expression)
    {
        if (expression is not SwitchExpressionSyntax { Arms.Count: > 0 } switchExpression ||
            switchExpression.Arms[switchExpression.Arms.Count - 1].Pattern is not DiscardPatternSyntax)
        {
            return null;
        }

        var localName = reserveGeneratedLocal("__sir_derived");
        addExpressionPrelude(DotBoxDRpcJsonLowerer.SetStatement(
            localName,
            lowerExpression(switchExpression.Arms[switchExpression.Arms.Count - 1].Expression)));

        var value = lowerExpression(switchExpression.GoverningExpression);
        for (var i = switchExpression.Arms.Count - 2; i >= 0; i--)
        {
            var arm = switchExpression.Arms[i];
            if (arm.WhenClause is not null || TryLowerSwitchCondition(arm.Pattern, value) is not { } condition)
            {
                throw unsupported();
            }

            addExpressionPrelude(DotBoxDRpcJsonLowerer.Obj(
                ("op", DotBoxDRpcJsonLowerer.Str("if")),
                ("condition", condition),
                ("then", "[" + DotBoxDRpcJsonLowerer.SetStatement(
                    localName,
                    lowerExpression(arm.Expression)) + "]"),
                ("else", "[]")));
        }

        return DotBoxDRpcJsonLowerer.Var(localName);
    }

    private string? TryLowerSwitchCondition(PatternSyntax pattern, string value)
        => pattern switch
        {
            ConstantPatternSyntax constant => DotBoxDRpcJsonLowerer.BinaryJson(
                "eq",
                value,
                lowerExpression(constant.Expression)),
            RelationalPatternSyntax relational => DotBoxDRpcJsonLowerer.BinaryJson(
                RelationalOperator(relational),
                value,
                lowerExpression(relational.Expression)),
            _ => null
        };

    private static string RelationalOperator(RelationalPatternSyntax pattern)
        => pattern.OperatorToken.Kind() switch
        {
            SyntaxKind.GreaterThanToken => "gt",
            SyntaxKind.GreaterThanEqualsToken => "gte",
            SyntaxKind.LessThanToken => "lt",
            SyntaxKind.LessThanEqualsToken => "lte",
            _ => throw new NotSupportedException($"Unsupported switch pattern '{pattern}'.")
        };
}

using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static partial class DotBoxDConditionBodyModelFactory
{
    private static readonly ConditionLowerer[] ConditionLowerers =
    [
        TryLowerParenthesized,
        TryLowerConditionalExpression,
        TryLowerLogicalNot,
        TryLowerLogicalAnd,
        TryLowerLogicalOr,
        TryLowerEagerAndExpression,
        TryLowerEagerOrExpression,
        TryLowerBoolXorExpression,
        TryLowerBoolEqualityExpression,
    ];

    private delegate bool ConditionLowerer(
        ExpressionSyntax expression,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDStatementBodyModel lowered);

    private static bool TryLowerParenthesized(
        ExpressionSyntax expression,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDStatementBodyModel lowered)
    {
        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            lowered = LowerCondition(parenthesized.Expression, whenTrue, whenFalse, context);
            return true;
        }

        lowered = null!;
        return false;
    }

    private static bool TryLowerConditionalExpression(
        ExpressionSyntax expression,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDStatementBodyModel lowered)
    {
        if (expression is ConditionalExpressionSyntax conditional)
        {
            lowered = LowerConditional(conditional, whenTrue, whenFalse, context);
            return true;
        }

        lowered = null!;
        return false;
    }

    private static bool TryLowerLogicalNot(
        ExpressionSyntax expression,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDStatementBodyModel lowered)
    {
        if (expression is PrefixUnaryExpressionSyntax unary &&
            unary.Kind() == SyntaxKind.LogicalNotExpression)
        {
            lowered = LowerNot(unary, whenTrue, whenFalse, context);
            return true;
        }

        lowered = null!;
        return false;
    }

    private static bool TryLowerLogicalAnd(
        ExpressionSyntax expression,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDStatementBodyModel lowered)
        => TryLowerBinaryKind(
            expression,
            SyntaxKind.LogicalAndExpression,
            static (binary, whenTrue, whenFalse, context) => LowerAnd(binary, whenTrue, whenFalse, context),
            whenTrue,
            whenFalse,
            context,
            out lowered);

    private static bool TryLowerLogicalOr(
        ExpressionSyntax expression,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDStatementBodyModel lowered)
        => TryLowerBinaryKind(
            expression,
            SyntaxKind.LogicalOrExpression,
            static (binary, whenTrue, whenFalse, context) => LowerOr(binary, whenTrue, whenFalse, context),
            whenTrue,
            whenFalse,
            context,
            out lowered);

    private static bool TryLowerEagerAndExpression(
        ExpressionSyntax expression,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDStatementBodyModel lowered)
        => TryLowerBinaryPredicate(
            expression,
            static (binary, context) => IsEagerAnd(binary, context),
            static (binary, whenTrue, whenFalse, context) => LowerEagerAnd(binary, whenTrue, whenFalse, context),
            whenTrue,
            whenFalse,
            context,
            out lowered);

    private static bool TryLowerEagerOrExpression(
        ExpressionSyntax expression,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDStatementBodyModel lowered)
        => TryLowerBinaryPredicate(
            expression,
            static (binary, context) => IsEagerOr(binary, context),
            static (binary, whenTrue, whenFalse, context) => LowerEagerOr(binary, whenTrue, whenFalse, context),
            whenTrue,
            whenFalse,
            context,
            out lowered);

    private static bool TryLowerBoolXorExpression(
        ExpressionSyntax expression,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDStatementBodyModel lowered)
        => TryLowerBinaryPredicate(
            expression,
            static (binary, context) => IsBoolXor(binary, context),
            static (binary, whenTrue, whenFalse, context) => LowerBoolXor(binary, whenTrue, whenFalse, context),
            whenTrue,
            whenFalse,
            context,
            out lowered);

    private static bool TryLowerBoolEqualityExpression(
        ExpressionSyntax expression,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDStatementBodyModel lowered)
        => TryLowerBinaryPredicate(
            expression,
            static (binary, context) => IsBoolEquality(binary, context),
            static (binary, whenTrue, whenFalse, context) => LowerBoolEquality(binary, whenTrue, whenFalse, context),
            whenTrue,
            whenFalse,
            context,
            out lowered);

    private static bool TryLowerBinaryKind(
        ExpressionSyntax expression,
        SyntaxKind kind,
        BinaryConditionLowerer lowerer,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDStatementBodyModel lowered)
        => TryLowerBinaryPredicate(
            expression,
            (binary, _) => binary.Kind() == kind,
            lowerer,
            whenTrue,
            whenFalse,
            context,
            out lowered);

    private static bool TryLowerBinaryPredicate(
        ExpressionSyntax expression,
        BinaryConditionPredicate predicate,
        BinaryConditionLowerer lowerer,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDStatementBodyModel lowered)
    {
        if (expression is BinaryExpressionSyntax binary &&
            predicate(binary, context))
        {
            lowered = lowerer(binary, whenTrue, whenFalse, context);
            return true;
        }

        lowered = null!;
        return false;
    }

    private delegate bool BinaryConditionPredicate(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context);

    private delegate DotBoxDStatementBodyModel BinaryConditionLowerer(
        BinaryExpressionSyntax binary,
        DotBoxDStatementBodyModel whenTrue,
        DotBoxDStatementBodyModel whenFalse,
        DotBoxDExpressionLoweringContext context);
}

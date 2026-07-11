using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDExpressionModelFactory
{
    private static readonly ExpressionLoweringResolver[] ExpressionLoweringResolvers =
    [
        TryLowerParenthesized,
        TryLowerUnary,
        TryLowerBinaryExpression,
        TryLowerInvocation,
        TryLowerPattern,
        TryLowerIdentifier,
        TryLowerMember,
        TryLowerInterpolatedString,
        TryLowerRecordCreation,
        TryLowerAnonymousObjectCreation,
        TryLowerLiteral,
    ];

    private delegate bool ExpressionLoweringResolver(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered);

    private static bool TryLowerBySyntax(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        foreach (var resolver in ExpressionLoweringResolvers)
        {
            if (resolver(expression, context, out lowered))
            {
                return true;
            }
        }

        lowered = null!;
        return false;
    }

    private static bool TryLowerParenthesized(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            lowered = Lower(parenthesized.Expression, context);
            return true;
        }

        lowered = null!;
        return false;
    }

    private static bool TryLowerUnary(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (expression is PrefixUnaryExpressionSyntax unary)
        {
            lowered = LowerUnary(unary, context);
            return true;
        }

        lowered = null!;
        return false;
    }

    private static bool TryLowerBinaryExpression(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (expression is BinaryExpressionSyntax binary)
        {
            lowered = LowerBinary(binary, context);
            return true;
        }

        lowered = null!;
        return false;
    }

    private static bool TryLowerInvocation(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (expression is InvocationExpressionSyntax invocation)
        {
            lowered = DotBoxDInvocationExpressionLowerer.Lower(invocation, context, part => Lower(part, context));
            return true;
        }

        lowered = null!;
        return false;
    }

    private static bool TryLowerPattern(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (expression is IsPatternExpressionSyntax pattern)
        {
            lowered = DotBoxDPatternExpressionLowerer.Lower(pattern, context, part => Lower(part, context));
            return true;
        }

        lowered = null!;
        return false;
    }

    private static bool TryLowerIdentifier(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (expression is not IdentifierNameSyntax identifier)
        {
            lowered = null!;
            return false;
        }

        lowered = TryLowerImplicitThisIdentifier(identifier, context) ??
            DotBoxDIdentifierExpressionLowerer.Lower(identifier, context);
        return true;
    }

    private static bool TryLowerMember(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (expression is not MemberAccessExpressionSyntax member)
        {
            lowered = null!;
            return false;
        }

        lowered = DotBoxDStringExpressionLowerer.TryLowerMember(member, context, part => Lower(part, context)) ??
            LowerMemberAccess(member, context);
        return true;
    }

    private static bool TryLowerInterpolatedString(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            lowered = DotBoxDInterpolatedStringExpressionLowerer.Lower(interpolated, part => Lower(part, context));
            return true;
        }

        lowered = null!;
        return false;
    }

    private static bool TryLowerRecordCreation(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (expression is BaseObjectCreationExpressionSyntax creation &&
            DotBoxDRecordCreationExpressionLowerer.TryLower(creation, context, part => Lower(part, context)) is { } record)
        {
            lowered = record;
            return true;
        }

        lowered = null!;
        return false;
    }

    private static bool TryLowerAnonymousObjectCreation(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (expression is AnonymousObjectCreationExpressionSyntax anonymous &&
            DotBoxDAnonymousObjectCreationExpressionLowerer.TryLower(anonymous, context, part => Lower(part, context)) is { } record)
        {
            lowered = record;
            return true;
        }

        lowered = null!;
        return false;
    }

    private static bool TryLowerLiteral(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        out DotBoxDExpressionModel lowered)
    {
        if (expression is LiteralExpressionSyntax literal)
        {
            lowered = DotBoxDLiteralExpressionLowerer.Lower(literal);
            return true;
        }

        lowered = null!;
        return false;
    }
}

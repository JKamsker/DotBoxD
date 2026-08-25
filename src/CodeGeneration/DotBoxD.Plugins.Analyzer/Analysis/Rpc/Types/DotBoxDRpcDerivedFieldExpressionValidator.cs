using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class DotBoxDRpcDerivedFieldExpressionValidator
{
    public static bool IsExpressionOverAssignedFields(
        ExpressionSyntax expression,
        ISet<string> assignedNames)
    {
        if (IsTerminalExpressionOverAssignedFields(expression, assignedNames) is { } terminal)
        {
            return terminal;
        }

        return expression switch
        {
            PrefixUnaryExpressionSyntax unary => IsSupportedUnary(unary) &&
                IsExpressionOverAssignedFields(unary.Operand, assignedNames),
            BinaryExpressionSyntax binary =>
                IsExpressionOverAssignedFields(binary.Left, assignedNames) &&
                IsExpressionOverAssignedFields(binary.Right, assignedNames),
            BaseObjectCreationExpressionSyntax creation =>
                CreationIsOverAssignedFields(creation, assignedNames),
            _ => false
        };
    }

    private static bool? IsTerminalExpressionOverAssignedFields(
        ExpressionSyntax expression,
        ISet<string> assignedNames)
        => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized =>
                IsExpressionOverAssignedFields(parenthesized.Expression, assignedNames),
            LiteralExpressionSyntax => true,
            IdentifierNameSyntax identifier => assignedNames.Contains(identifier.Identifier.ValueText),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } thisMember =>
                assignedNames.Contains(thisMember.Name.Identifier.ValueText),
            _ => null
        };

    private static bool CreationIsOverAssignedFields(
        BaseObjectCreationExpressionSyntax creation,
        ISet<string> assignedNames)
        => CreationHasConstructorArgumentsOrInitializer(creation) &&
           ArgumentsAreOverAssignedFields(creation.ArgumentList?.Arguments, assignedNames) &&
           InitializerIsOverAssignedFields(creation.Initializer, assignedNames);

    private static bool CreationHasConstructorArgumentsOrInitializer(BaseObjectCreationExpressionSyntax creation)
        => creation.ArgumentList is { Arguments.Count: > 0 } ||
           creation.Initializer is not null;

    private static bool ArgumentsAreOverAssignedFields(SeparatedSyntaxList<ArgumentSyntax>? arguments, ISet<string> assignedNames)
        => arguments is not { } argumentList ||
           argumentList.All(argument =>
               argument.RefKindKeyword.ValueText.Length == 0 &&
               IsExpressionOverAssignedFields(argument.Expression, assignedNames));

    private static bool InitializerIsOverAssignedFields(InitializerExpressionSyntax? initializer, ISet<string> assignedNames)
        => initializer is null ||
           initializer.Expressions.All(expression =>
               expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax, Right: { } value } &&
               IsExpressionOverAssignedFields(value, assignedNames));

    private static bool IsSupportedUnary(PrefixUnaryExpressionSyntax unary)
        => unary.IsKind(SyntaxKind.LogicalNotExpression) ||
           unary.IsKind(SyntaxKind.UnaryMinusExpression) ||
           unary.IsKind(SyntaxKind.UnaryPlusExpression);
}

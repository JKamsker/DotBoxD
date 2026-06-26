using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class DotBoxDRpcTypeMapper
{
    public static bool IsDerivedFromAssignedFields(
        RecordMember member,
        IReadOnlyList<RecordMember> fields,
        bool[] assigned)
    {
        if (member.Symbol is not IPropertySymbol
            {
                GetMethod: not null,
                SetMethod: null
            } property)
        {
            return false;
        }

        if (TryGetDerivedGetterExpression(property) is not { } body)
        {
            return property.DeclaringSyntaxReferences.Length == 0;
        }

        var assignedNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned[i])
            {
                assignedNames.Add(fields[i].Name);
            }
        }

        return IsExpressionOverAssignedFields(body, assignedNames);
    }

    private static bool IsExpressionOverAssignedFields(
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
            PrefixUnaryExpressionSyntax unary => IsSupportedUnary(unary) &&
                IsExpressionOverAssignedFields(unary.Operand, assignedNames),
            BinaryExpressionSyntax binary =>
                IsExpressionOverAssignedFields(binary.Left, assignedNames) &&
                IsExpressionOverAssignedFields(binary.Right, assignedNames),
            _ => false
        };

    private static bool IsSupportedUnary(PrefixUnaryExpressionSyntax unary)
        => unary.IsKind(SyntaxKind.LogicalNotExpression) ||
           unary.IsKind(SyntaxKind.UnaryMinusExpression) ||
           unary.IsKind(SyntaxKind.UnaryPlusExpression);

    private static ExpressionSyntax? TryGetDerivedGetterExpression(IPropertySymbol property)
    {
        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax declaration)
            {
                continue;
            }

            if (declaration.ExpressionBody is { } arrow)
            {
                return arrow.Expression;
            }

            var getter = declaration.AccessorList?.Accessors
                .FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
            if (getter?.ExpressionBody is { } getterArrow)
            {
                return getterArrow.Expression;
            }

            if (getter?.Body is { Statements.Count: 1 } getterBody &&
                getterBody.Statements[0] is ReturnStatementSyntax { Expression: { } returned })
            {
                return returned;
            }
        }

        return null;
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class DotBoxDRpcDerivedFieldExpressionValidator
{
    public static bool IsExpressionOverAssignedFields(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, ITypeSymbol> assignedFields,
        Compilation? compilation)
        => new AssignedFieldExpressionVisitor(assignedFields, compilation).Visit(expression);

    private sealed class AssignedFieldExpressionVisitor(
        IReadOnlyDictionary<string, ITypeSymbol> assignedFields,
        Compilation? compilation) : CSharpSyntaxVisitor<bool>
    {
        public override bool DefaultVisit(SyntaxNode node) => false;

        public override bool VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
            => Visit(node.Expression);

        public override bool VisitLiteralExpression(LiteralExpressionSyntax node) => true;

        public override bool VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
            => node.Contents.All(static content => content is InterpolatedStringTextSyntax);

        public override bool VisitIdentifierName(IdentifierNameSyntax node)
            => IsAssignedOrConstant(node, node.Identifier.ValueText);

        public override bool VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node.Expression is ThisExpressionSyntax or BaseExpressionSyntax)
            {
                return IsAssignedOrConstant(node, node.Name.Identifier.ValueText);
            }

            if (node.Name.Identifier.ValueText == "Count" &&
                TryGetAssignedFieldType(node.Expression, out var type))
            {
                return DotBoxDRpcTypeMapper.ListElementType(type) is not null && Visit(node.Expression);
            }

            return Visit(node.Expression);
        }

        public override bool VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
            => IsSupportedUnary(node) && Visit(node.Operand);

        public override bool VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
            => node.IsKind(SyntaxKind.SuppressNullableWarningExpression) && Visit(node.Operand);

        public override bool VisitCastExpression(CastExpressionSyntax node)
            => Visit(node.Expression);

        public override bool VisitBinaryExpression(BinaryExpressionSyntax node)
            => Visit(node.Left) && Visit(node.Right);

        public override bool VisitConditionalExpression(ConditionalExpressionSyntax node)
            => Visit(node.Condition) && Visit(node.WhenTrue) && Visit(node.WhenFalse);

        public override bool VisitInvocationExpression(InvocationExpressionSyntax node)
            => node.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" };

        public override bool VisitSwitchExpression(SwitchExpressionSyntax node)
            => Visit(node.GoverningExpression) &&
               node.Arms.Count > 0 &&
               node.Arms[node.Arms.Count - 1].Pattern is DiscardPatternSyntax &&
               node.Arms.All(arm => arm.WhenClause is null &&
                                    IsSupportedSwitchPattern(arm.Pattern) &&
                                    Visit(arm.Expression));

        public override bool VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            => VisitCreation(node);

        public override bool VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
            => VisitCreation(node);

        private bool VisitCreation(BaseObjectCreationExpressionSyntax creation)
            => CreationHasConstructorArgumentsOrInitializer(creation) &&
               ArgumentsAreOverAssignedFields(creation.ArgumentList?.Arguments) &&
               InitializerIsOverAssignedFields(creation.Initializer);

        private bool ArgumentsAreOverAssignedFields(SeparatedSyntaxList<ArgumentSyntax>? arguments)
            => arguments is not { } argumentList ||
               argumentList.All(argument =>
                   argument.RefKindKeyword.ValueText.Length == 0 && Visit(argument.Expression));

        private bool InitializerIsOverAssignedFields(InitializerExpressionSyntax? initializer)
            => initializer is null ||
               initializer.Expressions.All(expression =>
                   expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax, Right: { } value } &&
                   Visit(value));

        private bool TryGetAssignedFieldType(ExpressionSyntax expression, out ITypeSymbol type)
        {
            type = null!;
            var name = GetAssignedFieldName(expression);
            return name is not null && assignedFields.TryGetValue(name, out type);
        }

        private bool IsAssignedOrConstant(ExpressionSyntax expression, string name)
            => assignedFields.ContainsKey(name) || IsConstant(expression);

        private bool IsConstant(ExpressionSyntax expression)
            => compilation?.GetSemanticModel(expression.SyntaxTree).GetSymbolInfo(expression).Symbol is IFieldSymbol
            {
                IsConst: true,
                HasConstantValue: true
            };

        private static bool CreationHasConstructorArgumentsOrInitializer(BaseObjectCreationExpressionSyntax creation)
            => creation.ArgumentList is { Arguments.Count: > 0 } || creation.Initializer is not null;

        private static string? GetAssignedFieldName(ExpressionSyntax expression)
            => expression switch
            {
                ParenthesizedExpressionSyntax parenthesized => GetAssignedFieldName(parenthesized.Expression),
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax } member =>
                    member.Name.Identifier.ValueText,
                _ => null
            };

        private static bool IsSupportedSwitchPattern(PatternSyntax pattern)
            => pattern is DiscardPatternSyntax or ConstantPatternSyntax or RelationalPatternSyntax;

        private static bool IsSupportedUnary(PrefixUnaryExpressionSyntax node)
            => node.IsKind(SyntaxKind.LogicalNotExpression) ||
               node.IsKind(SyntaxKind.UnaryMinusExpression) ||
               node.IsKind(SyntaxKind.UnaryPlusExpression);
    }
}

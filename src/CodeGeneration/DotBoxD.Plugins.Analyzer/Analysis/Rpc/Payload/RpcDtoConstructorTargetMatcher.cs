using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcDtoConstructorTargetMatcher
{
    public static bool IsMemberOrBackingFieldTarget(
        ExpressionSyntax target,
        RecordMember member,
        SemanticModel? model)
        => IsMemberTarget(target, member, model) || IsBackingFieldTarget(target, member, model);

    private static bool IsMemberTarget(ExpressionSyntax target, RecordMember member, SemanticModel? model)
    {
        if (model?.GetSymbolInfo(target).Symbol is { } symbol)
        {
            return SymbolEqualityComparer.Default.Equals(symbol, member.Symbol);
        }

        return TargetName(target) == member.Name;
    }

    private static bool IsBackingFieldTarget(ExpressionSyntax target, RecordMember member, SemanticModel? model)
    {
        if (member.Symbol is not IPropertySymbol { SetMethod: null } property ||
            model?.GetSymbolInfo(target).Symbol is not IFieldSymbol targetField ||
            DirectBackingField(property, model) is not { } backingField)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(targetField, backingField);
    }

    private static IFieldSymbol? DirectBackingField(IPropertySymbol property, SemanticModel model)
    {
        if (DotBoxDRpcTypeMapper.TryGetDerivedGetterExpression(property) is not { } expression)
        {
            return null;
        }

        var expressionModel = model.Compilation.ContainsSyntaxTree(expression.SyntaxTree)
            ? model.Compilation.GetSemanticModel(expression.SyntaxTree)
            : null;

        return expressionModel?.GetSymbolInfo(StripParentheses(expression)).Symbol is IFieldSymbol field &&
               SymbolEqualityComparer.Default.Equals(field.ContainingType, property.ContainingType)
            ? field
            : null;
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static string? TargetName(ExpressionSyntax target)
        => target switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } =>
                name.Identifier.ValueText,
            _ => null,
        };
}

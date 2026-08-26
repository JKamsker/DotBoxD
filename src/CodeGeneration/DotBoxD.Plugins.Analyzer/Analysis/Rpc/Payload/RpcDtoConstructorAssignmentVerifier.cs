using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcDtoConstructorAssignmentVerifier
{
    public static bool TryAssignConstructorParameter(
        IMethodSymbol constructor,
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        IParameterSymbol parameter,
        Compilation? compilation)
        => TryAssignConstructorParameter(constructor, fields, assigned, parameter, compilation, out _);

    public static bool TryAssignConstructorParameter(
        IMethodSymbol constructor,
        IReadOnlyList<RecordMember> fields,
        bool[] assigned,
        IParameterSymbol parameter,
        Compilation? compilation,
        out int fieldIndex)
    {
        fieldIndex = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
        if (fieldIndex < 0)
        {
            return parameter.HasExplicitDefaultValue;
        }

        if (parameter.RefKind != RefKind.None)
        {
            assigned[fieldIndex] = true;
            return true;
        }

        if (assigned[fieldIndex] ||
            !ConstructorPreservesMember(constructor, fields[fieldIndex], parameter, compilation))
        {
            return false;
        }

        assigned[fieldIndex] = true;
        return true;
    }

    public static bool ConstructorPreservesMember(
        IMethodSymbol constructor,
        RecordMember member,
        IParameterSymbol parameter,
        Compilation? compilation)
    {
        if (DotBoxDRpcTypeMapper.IsObjectInitializerWritable(member, compilation) ||
            constructor.IsImplicitlyDeclared)
        {
            return true;
        }

        if (constructor.DeclaringSyntaxReferences.Length == 0)
        {
            return true;
        }

        foreach (var reference in constructor.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not ConstructorDeclarationSyntax declaration)
            {
                continue;
            }

            var model = compilation?.ContainsSyntaxTree(declaration.SyntaxTree) == true
                ? compilation.GetSemanticModel(declaration.SyntaxTree)
                : null;
            if (ConstructorBodyPreservesMember(declaration, member, parameter, model))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ConstructorBodyPreservesMember(
        ConstructorDeclarationSyntax declaration,
        RecordMember member,
        IParameterSymbol parameter,
        SemanticModel? model)
    {
        if (declaration.Initializer?.IsKind(SyntaxKind.ThisConstructorInitializer) == true)
        {
            return false;
        }

        var matched = false;
        foreach (var assignment in ConstructorAssignments(declaration))
        {
            if (!IsMemberTarget(assignment.Left, member, model) &&
                !IsBackingFieldTarget(assignment.Left, member, model))
            {
                continue;
            }

            if (matched ||
                !IsDirectConstructorAssignment(declaration, assignment) ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                !PreservesParameter(assignment.Right, parameter, model))
            {
                return false;
            }

            matched = true;
        }

        return matched;
    }

    private static IEnumerable<AssignmentExpressionSyntax> ConstructorAssignments(
        ConstructorDeclarationSyntax declaration)
    {
        if (declaration.ExpressionBody?.Expression is { } expression)
        {
            foreach (var assignment in expression.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
            {
                yield return assignment;
            }
        }

        if (declaration.Body is null)
        {
            yield break;
        }

        foreach (var assignment in declaration.Body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            yield return assignment;
        }
    }

    private static bool IsDirectConstructorAssignment(
        ConstructorDeclarationSyntax declaration,
        AssignmentExpressionSyntax assignment)
    {
        if (declaration.ExpressionBody?.Expression == assignment)
        {
            return true;
        }

        return assignment.Parent is ExpressionStatementSyntax statement &&
            statement.Parent == declaration.Body;
    }

    private static bool IsMemberTarget(
        ExpressionSyntax target,
        RecordMember member,
        SemanticModel? model)
    {
        if (model?.GetSymbolInfo(target).Symbol is { } symbol)
        {
            return SymbolEqualityComparer.Default.Equals(symbol, member.Symbol);
        }

        return TargetName(target) == member.Name;
    }

    private static bool PreservesParameter(
        ExpressionSyntax expression,
        IParameterSymbol parameter,
        SemanticModel? model)
    {
        expression = StripParentheses(expression);
        if (model?.GetSymbolInfo(expression).Symbol is { } symbol)
        {
            return SymbolEqualityComparer.Default.Equals(symbol, parameter);
        }

        if (expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.ValueText == parameter.Name;
        }

        return expression is ConditionalExpressionSyntax conditional &&
            TryEvaluateBoolean(conditional.Condition, parameter.ContainingSymbol, model) is { } condition &&
            PreservesParameter(condition ? conditional.WhenTrue : conditional.WhenFalse, parameter, model);
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool? TryEvaluateBoolean(
        ExpressionSyntax expression,
        ISymbol containingSymbol,
        SemanticModel? model)
    {
        expression = StripParentheses(expression);
        if (expression is LiteralExpressionSyntax { Token.Value: bool literal })
        {
            return literal;
        }

        if (expression.IsKind(SyntaxKind.LogicalNotExpression) &&
            expression is PrefixUnaryExpressionSyntax unary &&
            TryEvaluateBoolean(unary.Operand, containingSymbol, model) is { } operand)
        {
            return !operand;
        }

        return OptionalBoolDefault(expression, containingSymbol, model);
    }

    private static bool? OptionalBoolDefault(
        ExpressionSyntax expression,
        ISymbol containingSymbol,
        SemanticModel? model)
    {
        var symbol = model?.GetSymbolInfo(expression).Symbol;

        return symbol is IParameterSymbol
        {
            ContainingSymbol: var owner,
            HasExplicitDefaultValue: true,
            ExplicitDefaultValue: bool value
        } &&
            SymbolEqualityComparer.Default.Equals(owner, containingSymbol)
            ? value
            : null;
    }

    private static string? TargetName(ExpressionSyntax target)
        => target switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } =>
                name.Identifier.ValueText,
            _ => null,
        };

    private static bool IsBackingFieldTarget(
        ExpressionSyntax target,
        RecordMember member,
        SemanticModel? model)
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

        expression = StripParentheses(expression);
        return model.GetSymbolInfo(expression).Symbol is IFieldSymbol field &&
            SymbolEqualityComparer.Default.Equals(field.ContainingType, property.ContainingType)
                ? field
                : null;
    }
}

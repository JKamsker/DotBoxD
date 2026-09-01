using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcDtoConstructorMemberAssignmentInspector
{
    public static bool BodyPreservesMember(
        ConstructorDeclarationSyntax declaration,
        RecordMember member,
        IParameterSymbol parameter,
        SemanticModel? model)
    {
        var matched = false;
        foreach (var assignment in ConstructorAssignments(declaration))
        {
            if (RpcDtoConstructorTargetMatcher.IsMemberOrBackingFieldTarget(assignment.Left, member, model))
            {
                if (!IsPreservingDirectMemberAssignment(
                        declaration,
                        assignment,
                        member,
                        parameter,
                        model,
                        matched))
                {
                    return false;
                }

                matched = true;
                continue;
            }

            if (!TryGetTupleAssignmentMemberSource(declaration, assignment, member, model, out var source))
            {
                continue;
            }

            if (matched || source is null || !PreservesParameter(source, parameter, declaration, model))
            {
                return false;
            }

            matched = true;
        }

        return matched;
    }

    private static bool IsPreservingDirectMemberAssignment(
        ConstructorDeclarationSyntax declaration,
        AssignmentExpressionSyntax assignment,
        RecordMember member,
        IParameterSymbol parameter,
        SemanticModel? model,
        bool alreadyMatched)
        => !alreadyMatched &&
            IsDirectConstructorAssignment(declaration, assignment) &&
            assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            PreservesParameterOrDefaultState(assignment.Right, member, parameter, declaration, model);

    private static bool TryGetTupleAssignmentMemberSource(
        ConstructorDeclarationSyntax declaration,
        AssignmentExpressionSyntax assignment,
        RecordMember member,
        SemanticModel? model,
        out ExpressionSyntax? source)
    {
        source = null;
        if (!IsDirectConstructorAssignment(declaration, assignment) ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            StripParentheses(assignment.Left) is not TupleExpressionSyntax targets ||
            StripParentheses(assignment.Right) is not TupleExpressionSyntax values ||
            targets.Arguments.Count != values.Arguments.Count)
        {
            return false;
        }

        var memberIndex = -1;
        for (var i = 0; i < targets.Arguments.Count; i++)
        {
            if (!RpcDtoConstructorTargetMatcher.IsMemberOrBackingFieldTarget(
                    targets.Arguments[i].Expression,
                    member,
                    model))
            {
                continue;
            }

            if (memberIndex >= 0)
            {
                return true;
            }

            memberIndex = i;
        }

        if (memberIndex >= 0)
        {
            source = values.Arguments[memberIndex].Expression;
            return true;
        }

        return false;
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

        if (assignment.Parent is not ExpressionStatementSyntax statement)
        {
            return false;
        }

        for (SyntaxNode? parent = statement.Parent; parent is BlockSyntax block; parent = block.Parent)
        {
            if (block == declaration.Body)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PreservesParameterOrDefaultState(
        ExpressionSyntax expression,
        RecordMember member,
        IParameterSymbol parameter,
        ConstructorDeclarationSyntax declaration,
        SemanticModel? model)
        => PreservesParameter(expression, parameter, declaration, model) ||
           StripParentheses(expression) is BinaryExpressionSyntax coalesce &&
           coalesce.IsKind(SyntaxKind.CoalesceExpression) &&
           PreservesParameter(coalesce.Left, parameter, declaration, model) &&
           RpcDtoConstructorTargetMatcher.IsMemberOrBackingFieldTarget(coalesce.Right, member, model);

    private static bool PreservesParameter(
        ExpressionSyntax expression,
        IParameterSymbol parameter,
        ConstructorDeclarationSyntax declaration,
        SemanticModel? model)
    {
        expression = StripIdentityConversions(StripParentheses(expression), model);
        if (model?.GetSymbolInfo(expression).Symbol is { } symbol)
        {
            if (SymbolEqualityComparer.Default.Equals(symbol, parameter))
            {
                return true;
            }

            if (symbol is ILocalSymbol local &&
                RpcDtoConstructorLocalAliasInspector.PreservesParameter(
                    local,
                    declaration,
                    model,
                    initializer => PreservesParameter(initializer, parameter, declaration, model)))
            {
                return true;
            }
        }

        if (expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.ValueText == parameter.Name;
        }

        return expression is ConditionalExpressionSyntax conditional &&
            TryEvaluateBoolean(conditional.Condition, parameter.ContainingSymbol, model) is { } condition &&
            PreservesParameter(
                condition ? conditional.WhenTrue : conditional.WhenFalse,
                parameter,
                declaration,
                model);
    }

    private static ExpressionSyntax StripIdentityConversions(ExpressionSyntax expression, SemanticModel? model)
    {
        while (model?.GetOperation(expression) is IConversionOperation { Conversion.IsIdentity: true } conversion &&
               conversion.Operand.Syntax is ExpressionSyntax operand)
        {
            expression = StripParentheses(operand);
        }

        return expression;
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

}

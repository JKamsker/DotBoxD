using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.UI;

internal sealed class UiRecordConstructorAssignments(IMethodSymbol constructor, SemanticModel model, CancellationToken token)
{
    private readonly HashSet<ISymbol> _parameters = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> _members = new(SymbolEqualityComparer.Default);

    public void Add(ExpressionSyntax expression)
    {
        if (expression is not AssignmentExpressionSyntax assignment ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            throw UiRecordConstructionValidator.Unsupported();
        }

        var member = model.GetSymbolInfo(assignment.Left, token).Symbol
            ?? throw UiRecordConstructionValidator.Unsupported();
        RequireStoredMember(member);
        var source = StripParentheses(assignment.Right);
        var parameter = model.GetSymbolInfo(source, token).Symbol as IParameterSymbol
            ?? throw UiRecordConstructionValidator.Unsupported();
        RequireIdentityParameter(parameter, source);
        if (!_members.Add(member.OriginalDefinition) || !_parameters.Add(parameter.OriginalDefinition))
        {
            throw UiRecordConstructionValidator.Unsupported();
        }
    }

    public void RequireAllParametersAssigned()
    {
        if (_parameters.Count != constructor.Parameters.Length)
        {
            throw UiRecordConstructionValidator.Unsupported();
        }
    }

    private void RequireStoredMember(ISymbol member)
    {
        if (member is not (IPropertySymbol or IFieldSymbol) || member.IsStatic ||
            member.DeclaredAccessibility != Accessibility.Public ||
            !SymbolEqualityComparer.Default.Equals(member.ContainingType.OriginalDefinition, constructor.ContainingType.OriginalDefinition))
        {
            throw UiRecordConstructionValidator.Unsupported();
        }
    }

    private void RequireIdentityParameter(IParameterSymbol parameter, ExpressionSyntax source)
    {
        var type = model.GetTypeInfo(source, token);
        if (!SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol.OriginalDefinition, constructor.OriginalDefinition) ||
            !SymbolEqualityComparer.Default.Equals(type.Type, type.ConvertedType))
        {
            throw UiRecordConstructionValidator.Unsupported();
        }
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}

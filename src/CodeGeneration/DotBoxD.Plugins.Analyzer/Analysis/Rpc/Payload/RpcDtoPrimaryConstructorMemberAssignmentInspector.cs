using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcDtoPrimaryConstructorMemberAssignmentInspector
{
    public static bool PreservesMember(
        IMethodSymbol constructor,
        RecordMember member,
        IParameterSymbol parameter,
        Compilation? compilation)
    {
        if (compilation is null || !HasPrimaryConstructorParameter(constructor, parameter, compilation))
        {
            return false;
        }

        foreach (var reference in member.Symbol.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax();
            if (ReferencesParameter(InitializerValue(declaration), parameter, compilation) ||
                ReferencesParameter(GetterValue(declaration), parameter, compilation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPrimaryConstructorParameter(
        IMethodSymbol constructor,
        IParameterSymbol parameter,
        Compilation compilation)
    {
        foreach (var reference in constructor.ContainingType.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not ClassDeclarationSyntax { ParameterList: { } parameters } declaration ||
                !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
            {
                continue;
            }

            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var candidate in parameters.Parameters)
            {
                if (model.GetDeclaredSymbol(candidate) is IParameterSymbol source &&
                    MatchesParameter(source, parameter))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ReferencesParameter(
        ExpressionSyntax? expression,
        IParameterSymbol parameter,
        Compilation compilation)
    {
        if (expression is null || !compilation.ContainsSyntaxTree(expression.SyntaxTree))
        {
            return false;
        }

        var model = compilation.GetSemanticModel(expression.SyntaxTree);
        return model.GetSymbolInfo(expression).Symbol is IParameterSymbol source &&
               MatchesParameter(source, parameter);
    }

    private static bool MatchesParameter(IParameterSymbol source, IParameterSymbol parameter)
        => SymbolEqualityComparer.Default.Equals(source, parameter) ||
           (string.Equals(source.Name, parameter.Name, StringComparison.Ordinal) &&
            SymbolEqualityComparer.Default.Equals(source.Type, parameter.Type) &&
            SymbolEqualityComparer.Default.Equals(source.ContainingType, parameter.ContainingType));

    private static ExpressionSyntax? InitializerValue(SyntaxNode declaration)
        => declaration switch
        {
            PropertyDeclarationSyntax { Initializer.Value: { } value } => value,
            VariableDeclaratorSyntax { Initializer.Value: { } value } => value,
            _ => null,
        };

    private static ExpressionSyntax? GetterValue(SyntaxNode declaration)
        => declaration switch
        {
            PropertyDeclarationSyntax { ExpressionBody.Expression: { } value } => value,
            PropertyDeclarationSyntax { AccessorList.Accessors: { } accessors } => accessors
                .FirstOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration))?
                .ExpressionBody?
                .Expression,
            _ => null,
        };
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcDtoPrimaryConstructorMemberAssignmentInspector
{
    public static bool InitializerPreservesMember(
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
            if (InitializerValue(reference.GetSyntax()) is not { } initializer ||
                !compilation.ContainsSyntaxTree(initializer.SyntaxTree))
            {
                continue;
            }

            var model = compilation.GetSemanticModel(initializer.SyntaxTree);
            if (model.GetSymbolInfo(initializer).Symbol is IParameterSymbol source &&
                MatchesParameter(source, parameter))
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
}

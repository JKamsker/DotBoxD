using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcDtoPrimaryConstructorMemberAssignmentInspector
{
    public static bool GetterPreservesMember(
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
            if (GetterValue(reference.GetSyntax()) is not { } value ||
                !compilation.ContainsSyntaxTree(value.SyntaxTree))
            {
                continue;
            }

            var model = compilation.GetSemanticModel(value.SyntaxTree);
            if (model.GetSymbolInfo(value).Symbol is IParameterSymbol source &&
                SymbolEqualityComparer.Default.Equals(source, parameter))
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
                    SymbolEqualityComparer.Default.Equals(source, parameter))
                {
                    return true;
                }
            }
        }

        return false;
    }

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

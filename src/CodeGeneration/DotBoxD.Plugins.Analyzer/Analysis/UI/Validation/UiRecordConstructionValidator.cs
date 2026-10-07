using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.UI;

internal static class UiRecordConstructionValidator
{
    public static void Validate(BaseObjectCreationExpressionSyntax creation, INamedTypeSymbol type,
        Compilation compilation, CancellationToken token)
    {
        UiRecordStorageValidator.Validate(type, token);
        var model = compilation.GetSemanticModel(creation.SyntaxTree);
        var constructor = model.GetSymbolInfo(creation, token).Symbol as IMethodSymbol
            ?? throw Unsupported();
        ValidateConstructor(constructor, compilation, token);
    }

    private static void ValidateConstructor(IMethodSymbol constructor, Compilation compilation, CancellationToken token)
    {
        if (constructor.IsImplicitlyDeclared && constructor.Parameters.Length == 0)
        {
            return;
        }

        foreach (var reference in constructor.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax(token);
            if (syntax is RecordDeclarationSyntax { ParameterList: not null })
            {
                return;
            }

            if (syntax is ConstructorDeclarationSyntax declaration)
            {
                ValidateBody(declaration, constructor, compilation.GetSemanticModel(syntax.SyntaxTree), token);
                return;
            }
        }

        throw Unsupported();
    }

    private static void ValidateBody(ConstructorDeclarationSyntax declaration, IMethodSymbol constructor,
        SemanticModel model, CancellationToken token)
    {
        if (declaration.Initializer is not null)
        {
            throw Unsupported();
        }

        var assignments = new UiRecordConstructorAssignments(constructor, model, token);
        if (declaration.ExpressionBody is { } expressionBody)
        {
            assignments.Add(expressionBody.Expression);
        }
        else if (declaration.Body is { } body)
        {
            foreach (var statement in body.Statements)
            {
                if (statement is not ExpressionStatementSyntax expression)
                {
                    throw Unsupported();
                }

                assignments.Add(expression.Expression);
            }
        }
        else
        {
            throw Unsupported();
        }

        assignments.RequireAllParametersAssigned();
    }

    internal static NotSupportedException Unsupported() => new(
        "Local DTO construction requires plain stored fields/auto-properties and a constructor that only assigns unchanged parameters. Custom accessors, initializers, computed properties and inheritance require a remote handler.");
}

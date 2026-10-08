using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.UI;

internal static class UiRecordStorageValidator
{
    public static void Validate(INamedTypeSymbol type, CancellationToken token)
    {
        if (type.DeclaringSyntaxReferences.Length == 0 || HasExplicitLayout(type) ||
            type.BaseType?.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType) ||
            type.StaticConstructors.Any(c => !c.IsImplicitlyDeclared))
        {
            throw UiRecordConstructionValidator.Unsupported();
        }

        foreach (var member in type.GetMembers())
        {
            ValidateMember(member, token);
        }
    }

    private static bool HasExplicitLayout(INamedTypeSymbol type)
        => type.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute" &&
            attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is 2);

    private static void ValidateMember(ISymbol member, CancellationToken token)
    {
        if (member.IsImplicitlyDeclared || member is IFieldSymbol { IsConst: true })
        {
            return;
        }

        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax(token);
            if (syntax is VariableDeclaratorSyntax { Initializer: not null } ||
                syntax is PropertyDeclarationSyntax { Initializer: not null })
            {
                throw UiRecordConstructionValidator.Unsupported();
            }

            if (member is IPropertySymbol { IsStatic: false } && !IsStoredProperty(syntax))
            {
                throw UiRecordConstructionValidator.Unsupported();
            }
        }
    }

    private static bool IsStoredProperty(SyntaxNode syntax)
        => syntax is ParameterSyntax || syntax is PropertyDeclarationSyntax
        {
            ExpressionBody: null,
            AccessorList: { } accessors
        } && accessors.Accessors.All(a => a.Body is null && a.ExpressionBody is null);
}

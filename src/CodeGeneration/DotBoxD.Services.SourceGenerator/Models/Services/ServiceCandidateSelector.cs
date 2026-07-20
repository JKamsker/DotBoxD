using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class ServiceCandidateSelector
{
    public static bool TryGet(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken,
        out INamedTypeSymbol interfaceSymbol,
        out AttributeData serviceAttribute)
    {
        if (context.Node is not InterfaceDeclarationSyntax declaration ||
            context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol candidate ||
            !TryGetRpcServiceAttribute(candidate, cancellationToken, out serviceAttribute) ||
            !IsCanonicalDeclaration(declaration, candidate, serviceAttribute, cancellationToken))
        {
            interfaceSymbol = null!;
            serviceAttribute = null!;
            return false;
        }

        interfaceSymbol = candidate;
        return true;
    }

    private static bool TryGetRpcServiceAttribute(
        INamedTypeSymbol interfaceSymbol,
        CancellationToken cancellationToken,
        out AttributeData serviceAttribute)
    {
        foreach (var attribute in interfaceSymbol.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ServicesGeneratorTypeNames.IsRpcServiceAttribute(attribute.AttributeClass))
            {
                serviceAttribute = attribute;
                return true;
            }
        }

        serviceAttribute = null!;
        return false;
    }

    private static bool IsCanonicalDeclaration(
        InterfaceDeclarationSyntax declaration,
        INamedTypeSymbol interfaceSymbol,
        AttributeData serviceAttribute,
        CancellationToken cancellationToken)
    {
        var attributedDeclaration = serviceAttribute.ApplicationSyntaxReference?
            .GetSyntax(cancellationToken)
            .FirstAncestorOrSelf<InterfaceDeclarationSyntax>();
        if (attributedDeclaration is not null)
        {
            return IsSameDeclaration(declaration, attributedDeclaration);
        }

        var firstDeclaration = interfaceSymbol.DeclaringSyntaxReferences.Length > 0
            ? interfaceSymbol.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) as InterfaceDeclarationSyntax
            : null;
        return firstDeclaration is not null && IsSameDeclaration(declaration, firstDeclaration);
    }

    private static bool IsSameDeclaration(InterfaceDeclarationSyntax left, InterfaceDeclarationSyntax right)
        => left.SyntaxTree == right.SyntaxTree && left.Span == right.Span;
}

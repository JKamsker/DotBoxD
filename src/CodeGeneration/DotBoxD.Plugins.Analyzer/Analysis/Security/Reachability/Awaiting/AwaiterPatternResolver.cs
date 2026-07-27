using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class AwaiterPatternResolver
{
    public static IEnumerable<IMethodSymbol> Methods(
        SemanticModel semanticModel,
        ITypeSymbol awaitableType,
        int position,
        CancellationToken cancellationToken)
    {
        var instanceMember = FindMember<IMethodSymbol>(
            awaitableType,
            "GetAwaiter",
            static method => !method.IsStatic && method.Parameters.Length == 0);
        if (instanceMember is not null)
        {
            yield return instanceMember;
            yield break;
        }

        foreach (var method in ExtensionMethods(semanticModel, awaitableType, position, cancellationToken))
        {
            yield return method;
        }
    }

    public static IEnumerable<IMethodSymbol> ExtensionMethods(
        SemanticModel semanticModel,
        ITypeSymbol awaitableType,
        int position,
        CancellationToken cancellationToken)
    {
        var foundExtension = false;
        foreach (var symbol in semanticModel.LookupSymbols(
            position,
            name: "GetAwaiter",
            includeReducedExtensionMethods: true).OfType<IMethodSymbol>())
        {
            var method = symbol.ReducedFrom ?? symbol;
            if (!IsAwaiterExtensionFor(semanticModel.Compilation, awaitableType, method))
            {
                continue;
            }

            foundExtension = true;
            yield return method;
        }

        if (foundExtension)
        {
            yield break;
        }

        foreach (var method in SourceExtensionMethods(semanticModel, awaitableType, cancellationToken))
        {
            yield return method;
        }
    }

    public static TSymbol? FindMember<TSymbol>(
        ITypeSymbol type,
        string name,
        Func<TSymbol, bool> predicate)
        where TSymbol : class, ISymbol
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var member = current.GetMembers(name).OfType<TSymbol>().FirstOrDefault(predicate);
            if (member is not null)
            {
                return member;
            }
        }

        if (type.TypeKind == TypeKind.Interface && type is INamedTypeSymbol namedType)
        {
            foreach (var inheritedInterface in namedType.AllInterfaces)
            {
                var member = inheritedInterface.GetMembers(name).OfType<TSymbol>().FirstOrDefault(predicate);
                if (member is not null)
                {
                    return member;
                }
            }
        }

        return null;
    }

    private static IEnumerable<IMethodSymbol> SourceExtensionMethods(
        SemanticModel semanticModel,
        ITypeSymbol awaitableType,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in semanticModel.SyntaxTree
            .GetRoot(cancellationToken)
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>())
        {
            if (!string.Equals(declaration.Identifier.ValueText, "GetAwaiter", StringComparison.Ordinal) ||
                semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not IMethodSymbol method ||
                !IsAwaiterExtensionFor(semanticModel.Compilation, awaitableType, method))
            {
                continue;
            }

            yield return method;
        }
    }

    private static bool IsAwaiterExtensionFor(
        Compilation compilation,
        ITypeSymbol awaitableType,
        IMethodSymbol method)
    {
        if (!method.IsExtensionMethod ||
            method.Parameters.Length == 0 ||
            method.Parameters[0].RefKind != RefKind.None ||
            method.ReturnsVoid)
        {
            return false;
        }

        var receiverConversion = compilation.ClassifyConversion(awaitableType, method.Parameters[0].Type);
        return receiverConversion.IsImplicit;
    }
}

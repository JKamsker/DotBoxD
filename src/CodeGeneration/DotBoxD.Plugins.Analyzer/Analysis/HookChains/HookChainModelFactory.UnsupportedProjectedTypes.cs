using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static void RejectFileLocalProjectedType(ITypeSymbol? projectedType, SimpleNameSyntax terminalName)
    {
        if (projectedType is null ||
            FindFileLocalType(projectedType, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) is not { } fileLocalType)
        {
            return;
        }

        var message = "Remote RunLocal projected payload type '" +
            fileLocalType.ToDisplayString() +
            "' is file-local; generated hook-chain sources cannot name file-local types. " +
            "Use a named payload type that is visible to generated code, or project to supported scalar/record fields.";
        throw new HookChainUnsupportedDiagnosticException(
            new PluginKernelDiagnostic(message, PluginDiagnosticLocation.From(terminalName.GetLocation())));
    }

    private static INamedTypeSymbol? FindFileLocalType(ITypeSymbol type, HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(type))
        {
            return null;
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            return FindFileLocalType(arrayType.ElementType, visited);
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return null;
        }

        if (IsFileLocal(namedType))
        {
            return namedType;
        }

        foreach (var typeArgument in namedType.TypeArguments)
        {
            if (FindFileLocalType(typeArgument, visited) is { } fileLocalType)
            {
                return fileLocalType;
            }
        }

        if (!namedType.IsAnonymousType)
        {
            return null;
        }

        foreach (var member in namedType.GetMembers())
        {
            if (member is IPropertySymbol { IsStatic: false } property &&
                FindFileLocalType(property.Type, visited) is { } fileLocalType)
            {
                return fileLocalType;
            }
        }

        return null;
    }

    private static bool IsFileLocal(INamedTypeSymbol type)
    {
        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not SyntaxNode declaration)
            {
                continue;
            }

            var modifiers = declaration switch
            {
                BaseTypeDeclarationSyntax typeDeclaration => typeDeclaration.Modifiers,
                DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.Modifiers,
                _ => default,
            };
            foreach (var modifier in modifiers)
            {
                if (modifier.IsKind(SyntaxKind.FileKeyword))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private sealed class HookChainUnsupportedDiagnosticException(PluginKernelDiagnostic diagnostic) : Exception(diagnostic.Message)
    {
        public PluginKernelDiagnostic Diagnostic { get; } = diagnostic;
    }
}

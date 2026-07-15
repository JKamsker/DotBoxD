using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncGeneratedBuilderAliasResolver
{
    private const string BuilderSuffix = "Builder";

    public static bool TryBuilderType(
        SemanticModel model,
        ExpressionSyntax initializer,
        CancellationToken cancellationToken,
        out ExpressionSyntax builderType)
    {
        builderType = null!;
        initializer = HookChainAliasResolver.UnwrapTransparentExpression(initializer);
        if (initializer is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "Build",
                    Expression: { } factory
                }
            })
        {
            return false;
        }

        return TryBuilderTypeFromFactory(model, factory, cancellationToken, out builderType);
    }

    private static bool TryBuilderTypeFromFactory(
        SemanticModel model,
        ExpressionSyntax factory,
        CancellationToken cancellationToken,
        out ExpressionSyntax builderType)
    {
        var current = HookChainAliasResolver.UnwrapTransparentExpression(factory);
        while (current is InvocationExpressionSyntax invocation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (invocation.Expression is IdentifierNameSyntax)
            {
                return TryStaticImportBuilderType(current, model, cancellationToken, out builderType);
            }

            if (invocation.Expression is not MemberAccessExpressionSyntax { Expression: { } next })
            {
                break;
            }

            next = HookChainAliasResolver.UnwrapTransparentExpression(next);
            if (next is IdentifierNameSyntax identifier &&
                TryTypeAliasBuilderType(identifier, cancellationToken, out builderType))
            {
                return true;
            }

            current = next;
        }

        builderType = null!;
        return false;
    }

    private static bool TryTypeAliasBuilderType(
        IdentifierNameSyntax identifier,
        CancellationToken cancellationToken,
        out ExpressionSyntax builderType)
    {
        foreach (var directive in InScopeUsingDirectives(identifier, cancellationToken).Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (directive.Alias?.Name.Identifier.ValueText == identifier.Identifier.ValueText &&
                directive.Name is { } target &&
                IsBuilderType(target))
            {
                builderType = target;
                return true;
            }
        }

        builderType = null!;
        return false;
    }

    private static bool TryStaticImportBuilderType(
        SyntaxNode use,
        SemanticModel model,
        CancellationToken cancellationToken,
        out ExpressionSyntax builderType)
    {
        NameSyntax? found = null;
        foreach (var directive in InScopeUsingDirectives(use, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ||
                directive.Name is not { } target ||
                !IsBuilderType(target) ||
                IsBoundType(model, target, cancellationToken))
            {
                continue;
            }

            if (found is not null)
            {
                builderType = null!;
                return false;
            }

            found = target;
        }

        builderType = found!;
        return found is not null;
    }

    private static bool IsBoundType(
        SemanticModel model,
        NameSyntax typeName,
        CancellationToken cancellationToken)
    {
        var symbol = model.GetSymbolInfo(typeName, cancellationToken);
        return symbol.Symbol is not null ||
               symbol.CandidateSymbols.Length != 0 ||
               model.GetTypeInfo(typeName, cancellationToken).Type is { TypeKind: not TypeKind.Error };
    }

    private static bool IsBuilderType(NameSyntax name)
    {
        var identifier = name.GetLastToken().ValueText;
        return identifier.Length > BuilderSuffix.Length &&
               identifier.EndsWith(BuilderSuffix, StringComparison.Ordinal);
    }

    private static IEnumerable<UsingDirectiveSyntax> InScopeUsingDirectives(
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        if (node.SyntaxTree.GetRoot(cancellationToken) is CompilationUnitSyntax compilationUnit)
        {
            foreach (var directive in compilationUnit.Usings)
            {
                yield return directive;
            }
        }

        foreach (var declaration in node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse())
        {
            foreach (var directive in declaration.Usings)
            {
                yield return directive;
            }
        }
    }
}

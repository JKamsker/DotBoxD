using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

internal static class HookFireAsyncModelFactory
{
    public static HookFireAsyncModelResult? Create(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol contextType ||
            context.TargetNode is not TypeDeclarationSyntax declaration ||
            contextType.TypeParameters.Length > 0)
        {
            return null;
        }

        foreach (var attribute in context.Attributes)
        {
            if (!TryGetHookResultType(attribute, context.SemanticModel.Compilation, cancellationToken, out var resultType))
            {
                continue;
            }

            if (IsFileLocalOrNestedInFileLocal(contextType, cancellationToken))
            {
                return new HookFireAsyncModelResult(
                    null,
                    PluginKernelDiagnostic.Create(
                        declaration.Identifier,
                        $"hook context '{contextType.Name}' is file-local and cannot be referenced by generated "
                        + "HookRegistry.FireAsync(context) extensions; use a non-file-local context type or call "
                        + "HookRegistry.FireAsync<TContext, TResult>(...) from the same file"));
            }

            return new HookFireAsyncModelResult(
                new HookFireAsyncModel(
                    contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsEffectivelyPublic(contextType) && IsEffectivelyPublic(resultType) ? "public" : "internal"),
                null);
        }

        return null;
    }

    private static bool TryGetHookResultType(
        AttributeData attribute,
        Compilation compilation,
        CancellationToken cancellationToken,
        out INamedTypeSymbol resultType)
    {
        resultType = null!;
        if (!string.Equals(
                attribute.AttributeClass?.ToDisplayString(),
                DotBoxDMetadataNames.HookAttribute,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (attribute.ConstructorArguments.Length != 2)
        {
            return false;
        }

        if (attribute.ConstructorArguments[1].Value is not INamedTypeSymbol candidate)
        {
            return false;
        }

        if (!HookResultModelFactory.CanSatisfyHookResult(candidate, compilation, cancellationToken))
        {
            return false;
        }

        resultType = candidate;
        return true;
    }

    private static bool IsFileLocalOrNestedInFileLocal(
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            foreach (var reference in current.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration &&
                    declaration.Modifiers.Any(SyntaxKind.FileKeyword))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsEffectivelyPublic(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }
}

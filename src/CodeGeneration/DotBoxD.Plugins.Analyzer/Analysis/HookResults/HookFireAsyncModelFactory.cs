using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

internal static class HookFireAsyncModelFactory
{
    public static HookFireAsyncModel? Create(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol contextType ||
            contextType.TypeParameters.Length > 0)
        {
            return null;
        }

        foreach (var attribute in context.Attributes)
        {
            if (!string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDMetadataNames.HookAttribute,
                    StringComparison.Ordinal) ||
                attribute.ConstructorArguments.Length != 2 ||
                attribute.ConstructorArguments[1].Value is not INamedTypeSymbol resultType ||
                !HookResultModelFactory.CanSatisfyHookResult(
                    resultType,
                    context.SemanticModel.Compilation,
                    cancellationToken))
            {
                continue;
            }

            return new HookFireAsyncModel(
                contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsEffectivelyPublic(contextType) && IsEffectivelyPublic(resultType) ? "public" : "internal");
        }

        return null;
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

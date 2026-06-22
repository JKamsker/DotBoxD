using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookResults;

internal static class HookFireAsyncModelFactory
{
    public static HookFireAsyncModel? Create(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not INamedTypeSymbol contextType)
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
                attribute.ConstructorArguments[1].Value is not INamedTypeSymbol resultType)
            {
                continue;
            }

            return new HookFireAsyncModel(
                contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return null;
    }
}

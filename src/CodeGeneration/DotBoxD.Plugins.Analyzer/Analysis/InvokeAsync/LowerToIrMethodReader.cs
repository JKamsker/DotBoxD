using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal enum LoweredIrMethodKind
{
    AnonymousInvocation = 0,
}

internal static class LowerToIrMethodReader
{
    public static bool IsAnonymousInvocation(IMethodSymbol? method, Compilation compilation)
        => TryReadKind(method, compilation, out var value) &&
           value == (int)LoweredIrMethodKind.AnonymousInvocation;

    public static bool TryReadUnsupportedKind(IMethodSymbol? method, Compilation compilation, out int value)
        => TryReadKind(method, compilation, out value) &&
           value != (int)LoweredIrMethodKind.AnonymousInvocation;

    private static bool TryReadKind(IMethodSymbol? method, Compilation compilation, out int value)
    {
        value = 0;
        if (method is null)
        {
            return false;
        }

        if (compilation.GetTypeByMetadataName(DotBoxDMetadataNames.LowerToIrMethodAttribute) is not { } expected)
        {
            return false;
        }

        foreach (var attribute in method.OriginalDefinition.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is int attributeValue)
            {
                value = attributeValue;
                return true;
            }
        }

        return false;
    }
}

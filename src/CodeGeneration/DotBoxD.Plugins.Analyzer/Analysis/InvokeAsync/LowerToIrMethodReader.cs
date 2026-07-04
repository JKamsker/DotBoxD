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
    {
        if (method is null)
        {
            return false;
        }

        foreach (var attribute in method.OriginalDefinition.GetAttributes())
        {
            if (IsDotBoxDAttribute(attribute, compilation, DotBoxDMetadataNames.LowerToIrMethodAttribute) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is int value &&
                value == (int)LoweredIrMethodKind.AnonymousInvocation)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDotBoxDAttribute(AttributeData attribute, Compilation compilation, string metadataName)
        => compilation.GetTypeByMetadataName(metadataName) is { } expected &&
           SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected);
}

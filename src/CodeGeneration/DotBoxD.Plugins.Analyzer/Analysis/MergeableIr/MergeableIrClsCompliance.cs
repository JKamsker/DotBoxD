using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal static class MergeableIrClsCompliance
{
    public static bool IsEnabled(Compilation compilation)
    {
        var attributeType = compilation.GetTypeByMetadataName("System.CLSCompliantAttribute");
        if (attributeType is null)
        {
            return false;
        }

        return compilation.Assembly.GetAttributes().Any(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType) &&
            attribute.ConstructorArguments.Length == 1 &&
            attribute.ConstructorArguments[0].Value is true);
    }
}

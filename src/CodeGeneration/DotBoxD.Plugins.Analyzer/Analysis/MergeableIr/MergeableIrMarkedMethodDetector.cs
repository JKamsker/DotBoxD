using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal static class MergeableIrMarkedMethodDetector
{
    private const string ExtensionReceiverMessage =
        "extension receiver calls are not supported for mergeable IR lowering; use an instance method with a public LoweredPipelineStep or IRFunc primitive.";

    public static void ThrowIfUnsupportedExtensionReceiver(IMethodSymbol method, Compilation compilation)
    {
        if (IsExtensionReceiver(method) && HasMarkedParameter(method, compilation))
        {
            throw new NotSupportedException(ExtensionReceiverMessage);
        }
    }

    public static bool IsExtensionReceiver(IMethodSymbol method) =>
        method.IsExtensionMethod || method.ReducedFrom is not null;

    private static bool HasMarkedParameter(IMethodSymbol method, Compilation compilation) =>
        HasMarkedParameterCore(method, compilation) ||
        (method.ReducedFrom is { } definition && HasMarkedParameterCore(definition, compilation));

    private static bool HasMarkedParameterCore(IMethodSymbol method, Compilation compilation)
    {
        foreach (var parameter in method.Parameters)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                if (IsMergeableIrAttribute(attribute, compilation))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsMergeableIrAttribute(AttributeData attribute, Compilation compilation) =>
        MergeableIrAttributeReader.IsDotBoxDAttribute(
            attribute,
            compilation,
            DotBoxDMetadataNames.LowerToIrAttribute) ||
        MergeableIrAttributeReader.IsDotBoxDAttribute(
            attribute,
            compilation,
            DotBoxDMetadataNames.IRBodyOfAttribute);
}

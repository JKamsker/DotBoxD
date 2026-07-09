using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.MergeableIr;

internal sealed record MergeableIrBodyParameter(
    IParameterSymbol Parameter,
    string SourceParameterName,
    MergeableIrLoweredStepKind Kind,
    bool HasExplicitKind);

internal static class MergeableIrBodyParameterReader
{
    public static MergeableIrBodyParameter? Read(IMethodSymbol method, Compilation compilation)
    {
        MergeableIrBodyParameter? result = null;
        foreach (var parameter in method.Parameters)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                if (!MergeableIrAttributeReader.IsDotBoxDAttribute(
                        attribute,
                        compilation,
                        DotBoxDMetadataNames.IRBodyOfAttribute))
                {
                    continue;
                }

                if (result is not null)
                {
                    throw new NotSupportedException("only one [IRBodyOf] parameter is supported.");
                }

                result = Read(parameter, attribute);
            }
        }

        return result;
    }

    private static MergeableIrBodyParameter Read(IParameterSymbol parameter, AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length is not (1 or 2) ||
            attribute.ConstructorArguments[0].Value is not string sourceParameterName ||
            string.IsNullOrWhiteSpace(sourceParameterName))
        {
            throw new NotSupportedException("[IRBodyOf] must name the delegate parameter it lowers.");
        }

        if (attribute.ConstructorArguments.Length == 1)
        {
            return new MergeableIrBodyParameter(
                parameter,
                sourceParameterName,
                MergeableIrLoweredStepKind.Projection,
                false);
        }

        if (attribute.ConstructorArguments[1].Value is not int value)
        {
            throw new NotSupportedException("the [IRBodyOf] step kind is not supported.");
        }

        return new MergeableIrBodyParameter(
            parameter,
            sourceParameterName,
            MergeableIrStepKindReader.Parse(value),
            true);
    }
}

internal static class MergeableIrStepKindReader
{
    public static MergeableIrLoweredStepKind Parse(int value)
        => value switch
        {
            0 => MergeableIrLoweredStepKind.Filter,
            1 => MergeableIrLoweredStepKind.Projection,
            _ => throw new NotSupportedException("the lowered step kind is not supported.")
        };
}

internal static class MergeableIrAttributeReader
{
    public static bool IsDotBoxDAttribute(AttributeData attribute, Compilation compilation, string metadataName)
        => compilation.GetTypeByMetadataName(metadataName) is { } expected &&
           SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected);
}

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class KernelMethodDescriptorPayloadParser
{
    private static bool TryDescriptorScalars(
        Dictionary<string, string> properties,
        out DescriptorScalarValues values)
    {
        values = default;
        if (!TryBool(properties, "allocates", out var allocates) ||
            !TryString(properties, "contextType", out var contextType) ||
            !TryString(properties, "methodMetadataName", out var methodMetadataName) ||
            !TryString(properties, "normalizedSignature", out var normalizedSignature) ||
            !TryString(properties, "returnType", out var returnType) ||
            !TryString(properties, "source", out var source) ||
            !TryInt(properties, "version", out var version))
        {
            return false;
        }

        values = new DescriptorScalarValues(
            allocates,
            contextType,
            methodMetadataName,
            normalizedSignature,
            returnType,
            source,
            version);
        return true;
    }

    private static bool TryDescriptorCollections(
        Dictionary<string, string> properties,
        out DescriptorCollectionValues values)
    {
        values = default;
        if (!TryStringArray(properties, "capabilities", out var capabilities) ||
            !TryStringArray(properties, "effects", out var effects) ||
            !TryParameters(properties, out var parameters))
        {
            return false;
        }

        values = new DescriptorCollectionValues(capabilities, effects, parameters);
        return true;
    }

    private readonly record struct DescriptorScalarValues(
        bool Allocates,
        string ContextType,
        string MethodMetadataName,
        string NormalizedSignature,
        string ReturnType,
        string Source,
        int Version);

    private readonly record struct DescriptorCollectionValues(
        string[] Capabilities,
        string[] Effects,
        KernelMethodDescriptorParameter[] Parameters);
}

namespace DotBoxD.Plugins.Analyzer;

using Microsoft.CodeAnalysis;

internal static class DotBoxDTypeNameReader
{
    public static string SandboxTypeName(ITypeSymbol type)
        => type.SpecialType switch {
            SpecialType.System_Boolean => DotBoxDGenerationNames.ManifestTypes.Bool,
            SpecialType.System_Int32 => DotBoxDGenerationNames.ManifestTypes.Int,
            SpecialType.System_Int64 => DotBoxDGenerationNames.ManifestTypes.Long,
            SpecialType.System_Double => DotBoxDGenerationNames.ManifestTypes.Double,
            SpecialType.System_String => DotBoxDGenerationNames.ManifestTypes.String,
            _ => DotBoxDGenerationNames.ManifestTypes.Unsupported
        };

    public static bool IsSupportedScalar(ITypeSymbol type)
        => !string.Equals(
            SandboxTypeName(type),
            DotBoxDGenerationNames.ManifestTypes.Unsupported,
            StringComparison.Ordinal);
}

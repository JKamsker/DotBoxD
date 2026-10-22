using DotBoxD.CodeGeneration.Services;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerExtensionTypeResolver
{
    public static INamedTypeSymbol? Resolve(INamedTypeSymbol contract)
    {
        var assembly = contract.ContainingAssembly;
        return assembly.GetTypeByMetadataName(
            GeneratedServiceTypeNames.Namespace + "." + GeneratedServiceTypeNames.Extensions(assembly.Name))
            ?? assembly.GetTypeByMetadataName(
                GeneratedServiceTypeNames.Namespace + "." + GeneratedServiceTypeNames.LegacyExtensions);
    }

    public static string TypeName(INamedTypeSymbol contract) =>
        Resolve(contract)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        ?? "global::" + GeneratedServiceTypeNames.Namespace + "." +
            GeneratedServiceTypeNames.Extensions(contract.ContainingAssembly.Name);
}

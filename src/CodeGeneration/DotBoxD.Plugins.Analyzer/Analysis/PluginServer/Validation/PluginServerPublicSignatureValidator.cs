using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerPublicSignatureValidator
{
    public static void Validate(
        INamedTypeSymbol serverType,
        INamedTypeSymbol worldType,
        INamedTypeSymbol controlServiceType,
        ITypeSymbol liveSettingUpdateType)
    {
        if (serverType.DeclaredAccessibility != Accessibility.Public)
        {
            return;
        }

        EnsurePublicSignatureType(worldType, "world interface");
        EnsurePublicSignatureType(controlServiceType, "control-plane contract");
        EnsurePublicSignatureType(liveSettingUpdateType, "live-setting update type");
    }

    private static void EnsurePublicSignatureType(ITypeSymbol type, string description)
    {
        if (type is IArrayTypeSymbol array)
        {
            EnsurePublicSignatureType(array.ElementType, description);
            return;
        }

        if (type is not INamedTypeSymbol named)
        {
            return;
        }

        for (INamedTypeSymbol? current = named; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                throw new NotSupportedException(
                    $"Generated plugin server public {description} '{type.ToDisplayString()}' must be public.");
            }
        }

        foreach (var argument in named.TypeArguments)
        {
            EnsurePublicSignatureType(argument, description);
        }
    }
}

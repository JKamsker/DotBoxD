using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static partial class PluginServerFacadeModelFactory
{
    private static void ValidatePublicFacadeSignatureTypes(
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

    private static void ValidateGeneratedSurfaceCollisions(
        INamedTypeSymbol worldType,
        IReadOnlyList<PluginServerForwardedMethod> methods,
        IReadOnlyList<PluginServerControlProperty> controls)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal)
        {
            "Services",
            "ServerExtensions",
            "Hooks",
            "Subscriptions",
            "WireClient",
            "StartAsync",
            "RunAsync",
            "HoldUntilShutdownAsync",
            "Dispose",
            "DisposeAsync",
            "InvokeAsync",
            "Get",
            "PluginId",
            "InvokeServerExtensionAsync",
            "EnsureAnonymousKernelAsync",
        };

        foreach (var method in methods)
        {
            if (reserved.Contains(method.Name))
            {
                throw new NotSupportedException(
                    $"Generated plugin server world '{worldType.ToDisplayString()}' member '{method.Name}' collides with the generated facade surface.");
            }
        }

        foreach (var control in controls)
        {
            if (reserved.Contains(control.Name))
            {
                throw new NotSupportedException(
                    $"Generated plugin server control '{control.Name}' collides with the generated facade surface.");
            }
        }
    }
}

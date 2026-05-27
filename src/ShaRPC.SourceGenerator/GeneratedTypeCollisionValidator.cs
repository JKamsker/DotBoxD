using Microsoft.CodeAnalysis;

namespace ShaRPC.SourceGenerator;

internal static class GeneratedTypeCollisionValidator
{
    public static string? GetCollisionReason(INamedTypeSymbol interfaceSymbol, Compilation compilation)
    {
        var ns = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : interfaceSymbol.ContainingNamespace.ToDisplayString();
        var serviceName = NamingHelpers.StripInterfacePrefix(interfaceSymbol.Name);

        var proxyName = serviceName + "Proxy";
        if (TypeExists(compilation, ns, proxyName))
        {
            return $"generated proxy type '{proxyName}' would collide with an existing type";
        }

        var dispatcherName = serviceName + "Dispatcher";
        if (TypeExists(compilation, ns, dispatcherName))
        {
            return $"generated dispatcher type '{dispatcherName}' would collide with an existing type";
        }

        if (NamingHelpers.CanGenerateAsyncSiblingInterface(interfaceSymbol.Name))
        {
            var siblingName = NamingHelpers.AsyncSiblingInterfaceName(interfaceSymbol.Name);
            if (TypeExists(compilation, ns, siblingName))
            {
                return $"generated async sibling interface '{siblingName}' would collide with an existing type";
            }
        }

        if (compilation.GetTypeByMetadataName("ShaRPC.Generated.ShaRpcGeneratedExtensions") is not null)
        {
            return "generated extension type 'ShaRPC.Generated.ShaRpcGeneratedExtensions' would collide with an existing type";
        }

        return null;
    }

    private static bool TypeExists(Compilation compilation, string ns, string typeName)
    {
        var metadataName = string.IsNullOrEmpty(ns) ? typeName : ns + "." + typeName;
        return compilation.GetTypeByMetadataName(metadataName) is not null;
    }
}

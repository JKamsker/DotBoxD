using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static partial class InvokeAsyncReceiverResolver
{
    private static string PluginServerInterfaceTypeName(INamedTypeSymbol worldType)
        => "global::DotBoxD.Abstractions.IPluginServer<" + TypeName(worldType) + ">";

    private static string ServerInterfaceTypeName(INamedTypeSymbol facadeType, INamedTypeSymbol worldType)
    {
        var ns = facadeType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : facadeType.ContainingNamespace.ToDisplayString() + ".";
        return "global::" + ns + ServerInterfaceName(worldType);
    }

    private static string ServerInterfaceName(INamedTypeSymbol worldType)
    {
        var name = worldType.Name;
        if (name.StartsWith("I", StringComparison.Ordinal) && name.Length > 1 && char.IsUpper(name[1]))
        {
            name = name.Substring(1);
        }

        if (name.EndsWith("Access", StringComparison.Ordinal) && name.Length > "Access".Length)
        {
            name = name.Substring(0, name.Length - "Access".Length);
        }

        return "I" + name + "Server";
    }

    private static string TypeName(INamedTypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}

using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class ServiceModelFactory
{
    private static string? GetConfiguredServiceName(AttributeData serviceAttribute)
    {
        foreach (var namedArg in serviceAttribute.NamedArguments)
        {
            if (namedArg.Key == "Name" && namedArg.Value.Value is string name)
            {
                return name;
            }
        }

        return null;
    }

    private static (string Source, bool IsError) BuildObsoleteAttribute(
        INamedTypeSymbol interfaceSymbol,
        Compilation compilation,
        CancellationToken ct)
    {
        var obsoleteAttributeSymbol = compilation.GetTypeByMetadataName("System.ObsoleteAttribute");
        foreach (var attribute in interfaceSymbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, obsoleteAttributeSymbol))
            {
                return ObsoleteAttributeFormatter.Format(attribute);
            }
        }

        return (string.Empty, false);
    }

    private static string GetNamespace(INamespaceSymbol namespaceSymbol)
    {
        if (namespaceSymbol.IsGlobalNamespace)
        {
            return string.Empty;
        }

        var parts = new Stack<string>();
        for (var current = namespaceSymbol; !current.IsGlobalNamespace; current = current.ContainingNamespace)
        {
            parts.Push(current.Name);
        }

        return string.Join(".", parts);
    }
}

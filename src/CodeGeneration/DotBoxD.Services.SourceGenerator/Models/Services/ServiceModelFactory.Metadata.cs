using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class ServiceModelFactory
{
    private static string BuildTypeAttributePrefix(
        INamedTypeSymbol interfaceSymbol,
        string experimentalAttributePrefix,
        CancellationToken ct)
    {
        var attributes = new StringBuilder(experimentalAttributePrefix);
        foreach (var attribute in interfaceSymbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (!IsSupportedOSPlatformAttribute(attribute))
            {
                continue;
            }

            attributes.Append("    [global::System.Runtime.Versioning.SupportedOSPlatformAttribute(");
            AppendStringArgument(attributes, attribute.ConstructorArguments[0]);
            attributes.AppendLine(")]");
        }

        return attributes.ToString();
    }

    private static bool IsSupportedOSPlatformAttribute(AttributeData attribute) =>
        attribute.AttributeClass is { } attributeType &&
        attributeType.ToDisplayString() == "System.Runtime.Versioning.SupportedOSPlatformAttribute" &&
        attribute.ConstructorArguments.Length == 1 &&
        ReturnTypeClassifier.IsTrustedFrameworkType(attributeType);

    private static void AppendStringArgument(StringBuilder sb, TypedConstant argument)
    {
        if (argument.Value is string value)
        {
            sb.Append('"').Append(Infrastructure.LiteralHelpers.EscapeStringLiteral(value)).Append('"');
        }
        else
        {
            sb.Append("null");
        }
    }

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

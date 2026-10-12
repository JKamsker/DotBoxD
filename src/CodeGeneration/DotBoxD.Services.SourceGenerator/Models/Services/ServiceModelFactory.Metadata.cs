using System.Collections.Generic;
using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class ServiceModelFactory
{
    private const string RequiresPreviewFeaturesAttribute =
        "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute";

    private static string BuildTypeAttributePrefix(
        INamedTypeSymbol interfaceSymbol,
        string experimentalAttributePrefix,
        CancellationToken ct)
    {
        var attributes = new StringBuilder(experimentalAttributePrefix);
        foreach (var attribute in interfaceSymbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            var isRequiresPreviewFeatures = IsRequiresPreviewFeaturesAttribute(attribute);
            if (!isRequiresPreviewFeatures && !PlatformAttributeFormatter.IsSupported(attribute))
            {
                continue;
            }

            if (isRequiresPreviewFeatures)
            {
                AppendRequiresPreviewFeaturesAttribute(attributes, attribute);
            }
            else if (PlatformAttributeFormatter.IsSupported(attribute))
            {
                attributes.Append("    ");
                PlatformAttributeFormatter.Append(attributes, attribute);
            }
        }

        return attributes.ToString();
    }

    private static bool IsRequiresPreviewFeaturesAttribute(AttributeData attribute) =>
        attribute.AttributeClass is { } type &&
        type.ToDisplayString() == RequiresPreviewFeaturesAttribute &&
        ReturnTypeClassifier.IsTrustedFrameworkType(type);

    private static void AppendRequiresPreviewFeaturesAttribute(StringBuilder attributes, AttributeData attribute)
    {
        var message = attribute.ConstructorArguments.Length == 1
            ? attribute.ConstructorArguments[0].Value as string
            : null;
        var hasArguments = false;
        attributes.Append("    [global::")
            .Append(RequiresPreviewFeaturesAttribute);

        if (message is not null)
        {
            attributes.Append("(\"")
                .Append(LiteralHelpers.EscapeStringLiteral(message))
                .Append('"');
            hasArguments = true;
        }

        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key != "Url" || namedArgument.Value.Value is not string url)
            {
                continue;
            }

            attributes.Append(hasArguments ? ", Url = \"" : "(Url = \"")
                .Append(LiteralHelpers.EscapeStringLiteral(url))
                .Append('"');
            hasArguments = true;
        }

        attributes.AppendLine(hasArguments ? ")]" : "]");
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

using System.Collections.Generic;
using System.Linq;
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
            if (!IsPlatformCompatibilityAttribute(attribute))
            {
                continue;
            }

            attributes.Append("    [global::").Append(attribute.AttributeClass!.ToDisplayString()).Append('(');
            var arguments = attribute.ConstructorArguments.Select(FormatPlatformArgument).ToList();
            arguments.AddRange(attribute.NamedArguments.Select(argument =>
                argument.Key + " = " + FormatPlatformArgument(argument.Value)));
            attributes.Append(string.Join(", ", arguments)).AppendLine(")]");
        }

        return attributes.ToString();
    }

    private static bool IsPlatformCompatibilityAttribute(AttributeData attribute) =>
        attribute.AttributeClass is { } attributeType &&
        attributeType.ToDisplayString() is
            "System.Runtime.Versioning.SupportedOSPlatformAttribute" or
            "System.Runtime.Versioning.UnsupportedOSPlatformAttribute" or
            "System.Runtime.Versioning.ObsoletedOSPlatformAttribute" &&
        ReturnTypeClassifier.IsTrustedFrameworkType(attributeType);

    private static string FormatPlatformArgument(TypedConstant argument)
        => argument.Value is string value
            ? "\"" + Infrastructure.LiteralHelpers.EscapeStringLiteral(value) + "\""
            : "null";

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

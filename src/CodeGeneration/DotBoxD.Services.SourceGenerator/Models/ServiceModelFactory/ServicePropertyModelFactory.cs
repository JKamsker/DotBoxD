using System.Collections.Generic;
using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class ServicePropertyModelFactory
{
    private static readonly SymbolDisplayFormat s_qualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static bool TryBuild(
        IPropertySymbol propertySymbol,
        CancellationToken ct,
        out ServicePropertyModel property,
        out DiagnosticLocation location)
    {
        ct.ThrowIfCancellationRequested();

        location = DiagnosticLocationFactory.FromSymbol(propertySymbol);
        if (ServiceShapeValidator.IsInstanceIdProperty(propertySymbol))
        {
            property = new ServicePropertyModel(
                IdentifierHelpers.EscapeIdentifier(propertySymbol.Name),
                GetImplementationType(propertySymbol),
                propertySymbol.Type.ToDisplayString(s_qualifiedFormat),
                ProxyType: null,
                BuildPropertyFlowAttributePrefix(propertySymbol, ct),
                IsInstanceId: true,
                SubService: null);
            return true;
        }

        if (propertySymbol.Type is not INamedTypeSymbol propertyType)
        {
            property = null!;
            location = default;
            return false;
        }

        ReturnTypeClassifier.TryGetSubServiceInfo(propertySymbol.Type, ct, out var subService);
        var propertyNamespace = GetNamespace(propertyType.ContainingNamespace);
        var proxyName = NamingHelpers.StripInterfacePrefix(propertyType.Name) + "Proxy";
        property = new ServicePropertyModel(
            IdentifierHelpers.EscapeIdentifier(propertySymbol.Name),
            GetImplementationType(propertySymbol),
            propertyType.ToDisplayString(s_qualifiedFormat),
            IdentifierHelpers.QualifyTypeName(propertyNamespace, proxyName),
            BuildPropertyFlowAttributePrefix(propertySymbol, ct),
            IsInstanceId: false,
            subService);
        return true;
    }

    private static string BuildPropertyFlowAttributePrefix(IPropertySymbol property, CancellationToken ct)
    {
        var attributes = new StringBuilder();
        foreach (var attr in property.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            switch (attr.AttributeClass?.ToDisplayString())
            {
                case "System.Diagnostics.CodeAnalysis.AllowNullAttribute":
                    AppendSimpleAttribute(
                        attributes,
                        "global::System.Diagnostics.CodeAnalysis.AllowNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.DisallowNullAttribute":
                    AppendSimpleAttribute(
                        attributes,
                        "global::System.Diagnostics.CodeAnalysis.DisallowNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.MaybeNullAttribute":
                    AppendSimpleAttribute(
                        attributes,
                        "global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullAttribute":
                    AppendSimpleAttribute(
                        attributes,
                        "global::System.Diagnostics.CodeAnalysis.NotNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute":
                    AppendStringArgumentAttribute(
                        attributes,
                        attr,
                        "global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute");
                    break;
            }
        }

        return attributes.ToString();
    }

    private static void AppendSimpleAttribute(StringBuilder sb, string attributeType)
    {
        sb.Append("[")
            .Append(attributeType)
            .AppendLine("]");
    }

    private static void AppendStringArgumentAttribute(
        StringBuilder sb,
        AttributeData attr,
        string attributeType)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not string value)
        {
            return;
        }

        sb.Append("[")
            .Append(attributeType)
            .Append("(\"")
            .Append(LiteralHelpers.EscapeStringLiteral(value))
            .AppendLine("\")]");
    }

    private static string GetImplementationType(IPropertySymbol propertySymbol) =>
        propertySymbol.ContainingType.ToDisplayString(s_qualifiedFormat);

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

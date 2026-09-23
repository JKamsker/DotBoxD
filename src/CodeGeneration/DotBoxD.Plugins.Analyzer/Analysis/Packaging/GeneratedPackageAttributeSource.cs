using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class GeneratedPackageAttributeSource
{
    private const string ExperimentalAttribute = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";
    private const string RequiresDynamicCodeAttribute = "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute";
    private const string RequiresUnreferencedCodeAttribute = "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute";

    public static EquatableArray<string> FromKernel(INamedTypeSymbol kernelType, Compilation compilation)
    {
        var attributes = new List<string>();
        foreach (var attribute in kernelType.GetAttributes())
        {
            if (IsFrameworkExperimentalAttribute(attribute, compilation) &&
                TryExperimentalAttribute(attribute) is { } source)
            {
                attributes.Add(source);
            }

            if (TryCodeRequirementAttribute(attribute, compilation) is { } codeRequirementSource)
            {
                attributes.Add(codeRequirementSource);
            }
        }

        return EquatableArray<string>.FromOwned([.. attributes]);
    }

    private static bool IsFrameworkExperimentalAttribute(AttributeData attribute, Compilation compilation)
        => IsFrameworkAttribute(attribute, compilation, ExperimentalAttribute);

    private static bool IsFrameworkAttribute(
        AttributeData attribute,
        Compilation compilation,
        string attributeMetadataName)
    {
        foreach (var reference in compilation.References)
        {
            var aliases = reference.Properties.Aliases;
            if (!aliases.IsDefaultOrEmpty && !aliases.Contains("global", StringComparer.Ordinal))
            {
                continue;
            }

            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly &&
                SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass,
                    assembly.GetTypeByMetadataName(attributeMetadataName)))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryExperimentalAttribute(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string diagnosticId)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append("[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(")
            .Append(LiteralReader.StringLiteral(diagnosticId));
        AppendUrlFormat(builder, attribute);
        builder.Append(")]");
        return builder.ToString();
    }

    private static string? TryCodeRequirementAttribute(AttributeData attribute, Compilation compilation)
    {
        var attributeMetadataName = attribute.AttributeClass?.ToDisplayString();
        if (attributeMetadataName is not (RequiresDynamicCodeAttribute or RequiresUnreferencedCodeAttribute) ||
            !IsFrameworkAttribute(attribute, compilation, attributeMetadataName) ||
            attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string message)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append("[global::")
            .Append(attributeMetadataName)
            .Append("(")
            .Append(LiteralReader.StringLiteral(message));
        AppendUrl(builder, attribute);
        builder.Append(")]");
        return builder.ToString();
    }

    private static void AppendUrlFormat(StringBuilder builder, AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "UrlFormat" && argument.Value.Value is string urlFormat)
            {
                builder.Append(", UrlFormat = ").Append(LiteralReader.StringLiteral(urlFormat));
            }
        }
    }

    private static void AppendUrl(StringBuilder builder, AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Url" && argument.Value.Value is string url)
            {
                builder.Append(", Url = ").Append(LiteralReader.StringLiteral(url));
            }
        }
    }
}

using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class GeneratedPackageAttributeSource
{
    private const string ExperimentalAttribute = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";

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
        }

        return EquatableArray<string>.FromOwned([.. attributes]);
    }

    private static bool IsFrameworkExperimentalAttribute(AttributeData attribute, Compilation compilation)
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
                    assembly.GetTypeByMetadataName(ExperimentalAttribute)))
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
}

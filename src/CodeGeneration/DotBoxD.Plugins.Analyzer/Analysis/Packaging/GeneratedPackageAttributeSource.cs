using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class GeneratedPackageAttributeSource
{
    private const string ExperimentalAttribute = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";

    public static EquatableArray<string> FromKernel(INamedTypeSymbol kernelType)
    {
        var attributes = new List<string>();
        foreach (var attribute in kernelType.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == ExperimentalAttribute &&
                TryExperimentalAttribute(attribute) is { } source)
            {
                attributes.Add(source);
            }
        }

        return EquatableArray<string>.FromOwned([.. attributes]);
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

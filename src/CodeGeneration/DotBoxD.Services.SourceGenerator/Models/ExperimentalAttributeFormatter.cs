using System.Text;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class ExperimentalAttributeFormatter
{
    private const string ExperimentalAttributeName = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";
    private const string GlobalExperimentalAttributeName =
        "global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute";

    public static ExperimentalAttributeInfo From(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != ExperimentalAttributeName ||
                attr.ConstructorArguments.Length == 0 ||
                attr.ConstructorArguments[0].Value is not string diagnosticId ||
                string.IsNullOrWhiteSpace(diagnosticId))
            {
                continue;
            }

            var sb = new StringBuilder();
            sb.Append("    [")
                .Append(GlobalExperimentalAttributeName)
                .Append("(\"")
                .Append(LiteralHelpers.EscapeStringLiteral(diagnosticId))
                .Append('"');

            if (TryGetUrlFormat(attr, out var urlFormat))
            {
                sb.Append(", UrlFormat = \"")
                    .Append(LiteralHelpers.EscapeStringLiteral(urlFormat))
                    .Append('"');
            }

            sb.AppendLine(")]");
            return new ExperimentalAttributeInfo(sb.ToString(), diagnosticId);
        }

        return ExperimentalAttributeInfo.None;
    }

    private static bool TryGetUrlFormat(AttributeData attr, out string urlFormat)
    {
        foreach (var argument in attr.NamedArguments)
        {
            if (argument.Key == "UrlFormat" && argument.Value.Value is string value)
            {
                urlFormat = value;
                return true;
            }
        }

        urlFormat = string.Empty;
        return false;
    }
}

internal readonly record struct ExperimentalAttributeInfo(string AttributePrefix, string DiagnosticId)
{
    public static ExperimentalAttributeInfo None { get; } = new(string.Empty, string.Empty);
}

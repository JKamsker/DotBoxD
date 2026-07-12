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

            foreach (var argument in attr.NamedArguments)
            {
                if (argument.Value.Value is not string value ||
                    argument.Key is not ("UrlFormat" or "Message"))
                {
                    continue;
                }

                sb.Append(", ")
                    .Append(argument.Key)
                    .Append(" = \"")
                    .Append(LiteralHelpers.EscapeStringLiteral(value))
                    .Append('"');
            }

            sb.AppendLine(")]");
            return new ExperimentalAttributeInfo(sb.ToString(), diagnosticId);
        }

        return ExperimentalAttributeInfo.None;
    }
}

internal readonly record struct ExperimentalAttributeInfo(string AttributePrefix, string DiagnosticId)
{
    public static ExperimentalAttributeInfo None { get; } = new(string.Empty, string.Empty);
}

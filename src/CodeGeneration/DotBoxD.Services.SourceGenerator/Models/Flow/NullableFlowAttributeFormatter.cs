using System.Text;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class NullableFlowAttributeFormatter
{
    public static bool TryAppendInlineAttribute(StringBuilder sb, AttributeData attr)
    {
        switch (attr.AttributeClass?.ToDisplayString())
        {
            case "System.Diagnostics.CodeAnalysis.AllowNullAttribute":
                AppendSimpleAttribute(
                    sb,
                    "global::System.Diagnostics.CodeAnalysis.AllowNullAttribute",
                    inline: true);
                return true;

            case "System.Diagnostics.CodeAnalysis.DisallowNullAttribute":
                AppendSimpleAttribute(
                    sb,
                    "global::System.Diagnostics.CodeAnalysis.DisallowNullAttribute",
                    inline: true);
                return true;

            case "System.Diagnostics.CodeAnalysis.MaybeNullAttribute":
                AppendSimpleAttribute(
                    sb,
                    "global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute",
                    inline: true);
                return true;

            case "System.Diagnostics.CodeAnalysis.NotNullAttribute":
                AppendSimpleAttribute(
                    sb,
                    "global::System.Diagnostics.CodeAnalysis.NotNullAttribute",
                    inline: true);
                return true;

            case "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute":
                AppendBooleanArgumentAttribute(
                    sb,
                    attr,
                    "global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute");
                return true;

            case "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute":
                AppendBooleanArgumentAttribute(
                    sb,
                    attr,
                    "global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute");
                return true;

            case "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute":
                AppendStringArgumentAttribute(
                    sb,
                    attr,
                    "global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute",
                    inline: true);
                return true;

            default:
                return false;
        }
    }

    public static bool TryAppendMemberAttribute(StringBuilder sb, AttributeData attr)
    {
        switch (attr.AttributeClass?.ToDisplayString())
        {
            case "System.Diagnostics.CodeAnalysis.AllowNullAttribute":
                AppendSimpleAttribute(
                    sb,
                    "global::System.Diagnostics.CodeAnalysis.AllowNullAttribute",
                    inline: false);
                return true;

            case "System.Diagnostics.CodeAnalysis.DisallowNullAttribute":
                AppendSimpleAttribute(
                    sb,
                    "global::System.Diagnostics.CodeAnalysis.DisallowNullAttribute",
                    inline: false);
                return true;

            case "System.Diagnostics.CodeAnalysis.MaybeNullAttribute":
                AppendSimpleAttribute(
                    sb,
                    "global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute",
                    inline: false);
                return true;

            case "System.Diagnostics.CodeAnalysis.NotNullAttribute":
                AppendSimpleAttribute(
                    sb,
                    "global::System.Diagnostics.CodeAnalysis.NotNullAttribute",
                    inline: false);
                return true;

            case "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute":
                AppendStringArgumentAttribute(
                    sb,
                    attr,
                    "global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute",
                    inline: false);
                return true;

            case "System.Diagnostics.CodeAnalysis.ExperimentalAttribute":
                AppendExperimentalAttribute(sb, attr);
                return true;

            default:
                return false;
        }
    }

    public static bool TryAppendReturnAttribute(StringBuilder sb, AttributeData attr)
    {
        switch (attr.AttributeClass?.ToDisplayString())
        {
            case "System.Diagnostics.CodeAnalysis.MaybeNullAttribute":
                AppendReturnSimpleAttribute(
                    sb,
                    "global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute");
                return true;

            case "System.Diagnostics.CodeAnalysis.NotNullAttribute":
                AppendReturnSimpleAttribute(
                    sb,
                    "global::System.Diagnostics.CodeAnalysis.NotNullAttribute");
                return true;

            case "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute":
                AppendReturnStringArgumentAttribute(
                    sb,
                    attr,
                    "global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute");
                return true;

            default:
                return false;
        }
    }

    private static void AppendSimpleAttribute(StringBuilder sb, string attributeType, bool inline)
    {
        sb.Append("[")
            .Append(attributeType)
            .Append("]");
        AppendSeparator(sb, inline);
    }

    private static void AppendBooleanArgumentAttribute(
        StringBuilder sb,
        AttributeData attr,
        string attributeType)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not bool value)
        {
            return;
        }

        sb.Append("[")
            .Append(attributeType)
            .Append("(")
            .Append(value ? "true" : "false")
            .Append(")] ");
    }

    private static void AppendStringArgumentAttribute(
        StringBuilder sb,
        AttributeData attr,
        string attributeType,
        bool inline)
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
            .Append("\")]");
        AppendSeparator(sb, inline);
    }

    private static void AppendReturnSimpleAttribute(StringBuilder sb, string attributeType)
    {
        sb.Append("[return: ")
            .Append(attributeType)
            .AppendLine("]");
    }

    private static void AppendReturnStringArgumentAttribute(
        StringBuilder sb,
        AttributeData attr,
        string attributeType)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not string value)
        {
            return;
        }

        sb.Append("[return: ")
            .Append(attributeType)
            .Append("(\"")
            .Append(LiteralHelpers.EscapeStringLiteral(value))
            .AppendLine("\")]");
    }

    private static void AppendExperimentalAttribute(StringBuilder sb, AttributeData attr)
    {
        if (attr.ConstructorArguments.Length != 1 ||
            attr.ConstructorArguments[0].Value is not string diagnosticId)
        {
            return;
        }

        sb.Append("[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(\"")
            .Append(LiteralHelpers.EscapeStringLiteral(diagnosticId))
            .Append('"');

        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg is { Key: "UrlFormat", Value.Value: string urlFormat })
            {
                sb.Append(", UrlFormat = \"")
                    .Append(LiteralHelpers.EscapeStringLiteral(urlFormat))
                    .Append('"');
            }
            else if (namedArg is { Key: "Message", Value.Value: string message })
            {
                sb.Append(", Message = \"")
                    .Append(LiteralHelpers.EscapeStringLiteral(message))
                    .Append('"');
            }
        }

        sb.AppendLine(")]");
    }

    private static void AppendSeparator(StringBuilder sb, bool inline)
    {
        if (inline)
        {
            sb.Append(' ');
        }
        else
        {
            sb.AppendLine();
        }
    }
}

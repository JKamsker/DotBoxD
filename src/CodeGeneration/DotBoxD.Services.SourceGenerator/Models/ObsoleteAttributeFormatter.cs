using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class ObsoleteAttributeFormatter
{
    public static (string Source, bool IsError) Format(AttributeData attr)
    {
        var message = attr.ConstructorArguments.Length > 0 &&
            attr.ConstructorArguments[0].Value is string value
                ? "\"" + Infrastructure.LiteralHelpers.EscapeStringLiteral(value) + "\""
                : null;
        var isError = attr.ConstructorArguments.Length > 1 &&
            attr.ConstructorArguments[1].Value is bool constructorIsError &&
            constructorIsError;

        var source = message is null
            ? FormatWithoutConstructorArguments(attr)
            : FormatWithConstructorArguments(attr, message, isError);
        return (source, isError);
    }

    private static string FormatWithoutConstructorArguments(AttributeData attr)
    {
        var sb = new StringBuilder("[global::System.ObsoleteAttribute");
        var hasArguments = AppendNamedArguments(sb, attr, hasArguments: false);
        sb.Append(hasArguments ? ")]" : "]");
        return sb.ToString();
    }

    private static string FormatWithConstructorArguments(AttributeData attr, string message, bool isError)
    {
        var sb = new StringBuilder("[global::System.ObsoleteAttribute(");
        sb.Append(message);
        if (isError)
        {
            sb.Append(", true");
        }

        AppendNamedArguments(sb, attr, hasArguments: true);
        sb.Append(")]");
        return sb.ToString();
    }

    private static bool AppendNamedArguments(StringBuilder sb, AttributeData attr, bool hasArguments)
    {
        foreach (var argument in attr.NamedArguments)
        {
            if (argument.Key is not ("DiagnosticId" or "UrlFormat"))
            {
                continue;
            }

            sb.Append(hasArguments ? ", " : "(");
            hasArguments = true;
            sb.Append(argument.Key).Append(" = ");
            AppendStringArgument(sb, argument.Value);
        }

        return hasArguments;
    }

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
}

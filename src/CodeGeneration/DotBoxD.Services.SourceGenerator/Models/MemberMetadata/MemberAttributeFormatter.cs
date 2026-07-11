using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class MemberAttributeFormatter
{
    public static string BuildPrefix(ISymbol symbol, CancellationToken ct)
    {
        var attributes = new StringBuilder();
        foreach (var attr in symbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (attr.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute")
            {
                AppendObsoleteAttribute(attributes, attr);
            }
        }

        return attributes.ToString();
    }

    private static void AppendObsoleteAttribute(StringBuilder sb, AttributeData attr)
    {
        sb.Append("[global::System.ObsoleteAttribute");
        var hasArguments = attr.ConstructorArguments.Length > 0;
        if (hasArguments)
        {
            sb.Append("(");
            AppendStringArgument(sb, attr.ConstructorArguments[0]);
            if (attr.ConstructorArguments.Length > 1 &&
                attr.ConstructorArguments[1].Value is bool isError)
            {
                sb.Append(", ").Append(isError ? "true" : "false");
            }
        }

        hasArguments = AppendObsoleteNamedArguments(sb, attr, hasArguments);
        if (hasArguments)
        {
            sb.Append(")");
        }

        sb.AppendLine("]");
    }

    private static bool AppendObsoleteNamedArguments(StringBuilder sb, AttributeData attr, bool hasArguments)
    {
        foreach (var namedArgument in attr.NamedArguments)
        {
            if (namedArgument.Key is not ("DiagnosticId" or "UrlFormat"))
            {
                continue;
            }

            sb.Append(hasArguments ? ", " : "(");
            hasArguments = true;
            sb.Append(namedArgument.Key).Append(" = ");
            AppendStringArgument(sb, namedArgument.Value);
        }

        return hasArguments;
    }

    private static void AppendStringArgument(StringBuilder sb, TypedConstant argument)
    {
        if (argument.Value is string value)
        {
            sb.Append("\"").Append(LiteralHelpers.EscapeStringLiteral(value)).Append("\"");
        }
        else
        {
            sb.Append("null");
        }
    }
}

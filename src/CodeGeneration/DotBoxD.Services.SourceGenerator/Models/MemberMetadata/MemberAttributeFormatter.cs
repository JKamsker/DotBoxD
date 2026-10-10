using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class MemberAttributeFormatter
{
    private const string RequiresAssemblyFilesAttribute =
        "System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute";
    private const string RequiresDynamicCodeAttribute =
        "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute";
    private const string RequiresPreviewFeaturesAttribute =
        "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute";
    private const string RequiresUnreferencedCodeAttribute =
        "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute";

    public static string BuildPrefix(ISymbol symbol, Compilation compilation, CancellationToken ct)
    {
        var attributes = new StringBuilder();
        foreach (var attr in symbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (attr.AttributeClass is not { } type ||
                !SymbolEqualityComparer.Default.Equals(type, compilation.GetTypeByMetadataName(type.ToDisplayString())))
            {
                continue;
            }

            var attributeType = type.ToDisplayString();
            if (attributeType == "System.ObsoleteAttribute")
            {
                AppendObsoleteAttribute(attributes, attr);
            }
            else if (PlatformAttributeFormatter.IsSupported(attr))
            {
                PlatformAttributeFormatter.Append(attributes, attr);
            }
            else if (attributeType is RequiresAssemblyFilesAttribute or
                RequiresDynamicCodeAttribute or
                RequiresUnreferencedCodeAttribute or
                RequiresPreviewFeaturesAttribute)
            {
                AppendCodeRequirementAttribute(attributes, attr, attributeType);
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

    private static void AppendCodeRequirementAttribute(
        StringBuilder sb,
        AttributeData attr,
        string attributeType)
    {
        var hasMessage = attr.ConstructorArguments.Length == 1;
        if (!HasSupportedCodeRequirementConstructor(attr, attributeType))
        {
            return;
        }

        sb.Append("[global::").Append(attributeType);
        if (hasMessage)
        {
            sb.Append("(");
            AppendStringArgument(sb, attr.ConstructorArguments[0]);
        }

        var hasNamedArguments = false;
        foreach (var namedArgument in attr.NamedArguments)
        {
            if (namedArgument.Key != "Url")
            {
                continue;
            }

            sb.Append(hasMessage ? ", Url = " : "(Url = ");
            AppendStringArgument(sb, namedArgument.Value);
            hasNamedArguments = true;
        }

        if (hasMessage || hasNamedArguments)
        {
            sb.Append(")");
        }

        sb.AppendLine("]");
    }

    private static bool HasSupportedCodeRequirementConstructor(
        AttributeData attr,
        string attributeType)
    {
        if (attr.ConstructorArguments.Length == 1)
        {
            return true;
        }

        return attr.ConstructorArguments.Length == 0 &&
            attributeType is RequiresAssemblyFilesAttribute or RequiresPreviewFeaturesAttribute;
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

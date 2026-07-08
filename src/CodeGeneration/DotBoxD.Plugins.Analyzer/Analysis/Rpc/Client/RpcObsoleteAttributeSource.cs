using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcObsoleteAttributeSource
{
    public static void Append(StringBuilder builder, INamedTypeSymbol sourceType, string indent)
    {
        foreach (var attribute in sourceType.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute")
            {
                AppendAttribute(builder, attribute, indent);
                return;
            }
        }
    }

    private static void AppendAttribute(StringBuilder builder, AttributeData attribute, string indent)
    {
        var source = new StringBuilder();
        source.Append(indent).Append("[global::System.ObsoleteAttribute");
        if (attribute.ConstructorArguments.Length == 0 &&
            !HasSupportedNamedArguments(attribute))
        {
            source.AppendLine("]");
            builder.Append(source);
            return;
        }

        source.Append('(');
        var needsSeparator = false;
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (!TryAppendArgument(source, argument, ref needsSeparator))
            {
                return;
            }
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (!TryAppendNamedArgument(source, argument, ref needsSeparator))
            {
                return;
            }
        }

        source.AppendLine(")]");
        builder.Append(source);
    }

    private static bool HasSupportedNamedArguments(AttributeData attribute)
        => attribute.NamedArguments.Any(static argument => argument.Key is "DiagnosticId" or "UrlFormat");

    private static bool TryAppendNamedArgument(
        StringBuilder builder,
        KeyValuePair<string, TypedConstant> argument,
        ref bool needsSeparator)
    {
        if (argument.Key is not ("DiagnosticId" or "UrlFormat"))
        {
            return true;
        }

        AppendSeparator(builder, ref needsSeparator);
        builder.Append(argument.Key).Append(" = ");
        return TryAppendArgumentValue(builder, argument.Value);
    }

    private static bool TryAppendArgument(StringBuilder builder, TypedConstant argument, ref bool needsSeparator)
    {
        AppendSeparator(builder, ref needsSeparator);
        return TryAppendArgumentValue(builder, argument);
    }

    private static bool TryAppendArgumentValue(StringBuilder builder, TypedConstant argument)
    {
        if (argument.Value is null)
        {
            builder.Append("null");
            return true;
        }

        switch (argument.Value)
        {
            case string value:
                builder.Append(LiteralReader.StringLiteral(value));
                return true;

            case bool value:
                builder.Append(value ? "true" : "false");
                return true;

            default:
                return false;
        }
    }

    private static void AppendSeparator(StringBuilder builder, ref bool needsSeparator)
    {
        if (needsSeparator)
        {
            builder.Append(", ");
        }
        else
        {
            needsSeparator = true;
        }
    }
}

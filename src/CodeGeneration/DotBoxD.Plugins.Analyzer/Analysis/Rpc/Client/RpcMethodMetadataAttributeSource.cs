using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcMethodMetadataAttributeSource
{
    public static void Append(StringBuilder builder, IMethodSymbol method, string indent)
    {
        foreach (var attribute in method.GetAttributes())
        {
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case "System.Diagnostics.CodeAnalysis.ExperimentalAttribute":
                    AppendAttribute(
                        builder,
                        attribute,
                        indent,
                        "global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute");
                    break;

                case "System.ObsoleteAttribute":
                    AppendAttribute(builder, attribute, indent, "global::System.ObsoleteAttribute");
                    break;
            }
        }
    }

    private static void AppendAttribute(
        StringBuilder builder,
        AttributeData attribute,
        string indent,
        string attributeType)
    {
        builder.Append(indent).Append('[').Append(attributeType);
        if (attribute.ConstructorArguments.Length > 0 || attribute.NamedArguments.Length > 0)
        {
            builder.Append('(');
            AppendConstructorArguments(builder, attribute);
            AppendNamedArguments(builder, attribute);
            builder.Append(')');
        }

        builder.AppendLine("]");
    }

    private static void AppendConstructorArguments(StringBuilder builder, AttributeData attribute)
    {
        for (var i = 0; i < attribute.ConstructorArguments.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            AppendValue(builder, attribute.ConstructorArguments[i]);
        }
    }

    private static void AppendNamedArguments(StringBuilder builder, AttributeData attribute)
    {
        for (var i = 0; i < attribute.NamedArguments.Length; i++)
        {
            if (attribute.ConstructorArguments.Length > 0 || i > 0)
            {
                builder.Append(", ");
            }

            var argument = attribute.NamedArguments[i];
            builder.Append(argument.Key).Append(" = ");
            AppendValue(builder, argument.Value);
        }
    }

    private static void AppendValue(StringBuilder builder, TypedConstant value)
    {
        switch (value.Value)
        {
            case null:
                builder.Append("null");
                break;

            case string text:
                builder.Append(LiteralReader.StringLiteral(text));
                break;

            case bool flag:
                builder.Append(flag ? "true" : "false");
                break;

            default:
                throw new NotSupportedException("Method metadata attribute arguments must be null, string, or bool values.");
        }
    }
}

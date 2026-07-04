using System.Text;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerFlowAttributeSource
{
    public static EquatableArray<string> ReturnAttributes(IMethodSymbol method)
        => AttributeLines(method.GetReturnTypeAttributes(), targetReturn: true);

    public static EquatableArray<string> PropertyAttributes(IPropertySymbol property)
        => AttributeLines(property.GetAttributes(), targetReturn: false);

    public static string ParameterAttributePrefix(IParameterSymbol parameter)
    {
        var builder = new StringBuilder();
        foreach (var attribute in parameter.GetAttributes())
        {
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case "System.Diagnostics.CodeAnalysis.AllowNullAttribute":
                    AppendSimpleAttributePrefix(
                        builder,
                        "global::System.Diagnostics.CodeAnalysis.AllowNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.DisallowNullAttribute":
                    AppendSimpleAttributePrefix(
                        builder,
                        "global::System.Diagnostics.CodeAnalysis.DisallowNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.MaybeNullAttribute":
                    AppendSimpleAttributePrefix(
                        builder,
                        "global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullAttribute":
                    AppendSimpleAttributePrefix(
                        builder,
                        "global::System.Diagnostics.CodeAnalysis.NotNullAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute":
                    AppendBooleanAttributePrefix(
                        builder,
                        attribute,
                        "global::System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute":
                    AppendBooleanAttributePrefix(
                        builder,
                        attribute,
                        "global::System.Diagnostics.CodeAnalysis.NotNullWhenAttribute");
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute":
                    AppendStringAttributePrefix(
                        builder,
                        attribute,
                        "global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute");
                    break;
            }
        }

        return builder.ToString();
    }

    public static void Append(StringBuilder builder, string indent, EquatableArray<string> attributes)
    {
        foreach (var attribute in attributes)
        {
            builder.Append(indent).AppendLine(attribute);
        }
    }

    private static EquatableArray<string> AttributeLines(
        IEnumerable<AttributeData> attributes,
        bool targetReturn)
    {
        var lines = new List<string>();
        foreach (var attribute in attributes)
        {
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case "System.Diagnostics.CodeAnalysis.MaybeNullAttribute":
                    lines.Add(SimpleAttribute(
                        "global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute",
                        targetReturn));
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullAttribute":
                    lines.Add(SimpleAttribute(
                        "global::System.Diagnostics.CodeAnalysis.NotNullAttribute",
                        targetReturn));
                    break;

                case "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute":
                    if (StringArgumentAttribute(
                        attribute,
                        "global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute",
                        targetReturn) is { } source)
                    {
                        lines.Add(source);
                    }

                    break;
            }
        }

        return new EquatableArray<string>(lines.ToArray());
    }

    private static string SimpleAttribute(string attributeType, bool targetReturn)
        => targetReturn
            ? "[return: " + attributeType + "]"
            : "[" + attributeType + "]";

    private static string? StringArgumentAttribute(
        AttributeData attribute,
        string attributeType,
        bool targetReturn)
    {
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string value)
        {
            return null;
        }

        var prefix = targetReturn ? "[return: " : "[";
        return prefix + attributeType + "(" + LiteralReader.StringLiteral(value) + ")]";
    }

    private static void AppendSimpleAttributePrefix(StringBuilder builder, string attributeType)
        => builder.Append('[').Append(attributeType).Append("] ");

    private static void AppendBooleanAttributePrefix(
        StringBuilder builder,
        AttributeData attribute,
        string attributeType)
    {
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not bool value)
        {
            return;
        }

        builder.Append('[')
            .Append(attributeType)
            .Append('(')
            .Append(value ? "true" : "false")
            .Append(")] ");
    }

    private static void AppendStringAttributePrefix(
        StringBuilder builder,
        AttributeData attribute,
        string attributeType)
    {
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string value)
        {
            return;
        }

        builder.Append('[')
            .Append(attributeType)
            .Append('(')
            .Append(LiteralReader.StringLiteral(value))
            .Append(")] ");
    }
}

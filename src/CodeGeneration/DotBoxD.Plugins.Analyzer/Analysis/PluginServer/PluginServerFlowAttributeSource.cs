using System.Text;
using DotBoxD.CodeGeneration.Shared.Defaults;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerFlowAttributeSource
{
    public static EquatableArray<string> MemberAttributes(IMethodSymbol method)
        => AttributeLines(method.GetAttributes(), targetReturn: false);

    public static EquatableArray<string> ReturnAttributes(IMethodSymbol method)
        => AttributeLines(method.GetReturnTypeAttributes(), targetReturn: true);

    public static EquatableArray<string> PropertyAttributes(IPropertySymbol property)
        => AttributeLines(property.GetAttributes(), targetReturn: false);

    public static EquatableArray<string> TypeAttributes(INamedTypeSymbol type)
        => AttributeLines(type.GetAttributes(), targetReturn: false);

    public static string ParameterAttributePrefix(IParameterSymbol parameter)
    {
        var builder = new StringBuilder();
        foreach (var attribute in parameter.GetAttributes())
        {
            if (CallerInfoAttributeFormatter.TryAppend(builder, attribute))
            {
                continue;
            }

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

    public static bool HasErrorObsoleteAttribute(IEnumerable<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute" &&
                attribute.ConstructorArguments.Length >= 2 &&
                attribute.ConstructorArguments[1].Value is true)
            {
                return true;
            }
        }

        return false;
    }

    private static EquatableArray<string> AttributeLines(
        IEnumerable<AttributeData> attributes,
        bool targetReturn)
    {
        var lines = new List<string>();
        foreach (var attribute in attributes)
        {
            if (AttributeLine(attribute, targetReturn) is { } line)
            {
                lines.Add(line);
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return new EquatableArray<string>(lines.ToArray());
    }

    private static string? AttributeLine(AttributeData attribute, bool targetReturn)
    {
        switch (attribute.AttributeClass?.ToDisplayString())
        {
            case "System.Diagnostics.CodeAnalysis.MaybeNullAttribute":
                return SimpleAttribute(
                    "global::System.Diagnostics.CodeAnalysis.MaybeNullAttribute",
                    targetReturn);

            case "System.Diagnostics.CodeAnalysis.NotNullAttribute":
                return SimpleAttribute(
                    "global::System.Diagnostics.CodeAnalysis.NotNullAttribute",
                    targetReturn);

            case "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute":
                return StringArgumentAttribute(
                    attribute,
                    "global::System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute",
                    targetReturn);

            case "System.ObsoleteAttribute":
                return MemberOnlyAttribute(targetReturn, ObsoleteAttribute(attribute));

            case "System.Diagnostics.CodeAnalysis.ExperimentalAttribute":
                return MemberOnlyAttribute(targetReturn, ExperimentalAttribute(attribute));

            default:
                return null;
        }
    }

    private static string? MemberOnlyAttribute(bool targetReturn, string? source) => targetReturn ? null : source;

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

    private static string? ObsoleteAttribute(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length > 2)
        {
            return null;
        }

        var arguments = new List<string>();
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Value is not (null or string or bool))
            {
                return null;
            }

            arguments.Add(LiteralReader.ObjectLiteral(argument.Value));
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Value.Value is not (null or string))
            {
                return null;
            }

            arguments.Add(argument.Key + " = " + LiteralReader.ObjectLiteral(argument.Value.Value));
        }

        return arguments.Count == 0
            ? "[global::System.ObsoleteAttribute]"
            : "[global::System.ObsoleteAttribute(" + string.Join(", ", arguments) + ")]";
    }

    private static string? ExperimentalAttribute(AttributeData attribute)
    {
        if (!TryExperimentalConstructorArguments(attribute, out var arguments) ||
            !TryAddExperimentalNamedArguments(attribute, arguments))
        {
            return null;
        }

        return "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(" + string.Join(", ", arguments) + ")]";
    }

    private static bool TryExperimentalConstructorArguments(AttributeData attribute, out List<string> arguments)
    {
        arguments = [];
        if (attribute.ConstructorArguments is { Length: not (1 or 2) } ||
            attribute.ConstructorArguments[0].Value is not string diagnosticId)
        {
            return false;
        }

        arguments.Add(LiteralReader.StringLiteral(diagnosticId));
        if (attribute.ConstructorArguments.Length == 2)
        {
            return TryAddOptionalStringArgument(attribute.ConstructorArguments[1], arguments);
        }

        return true;
    }

    private static bool TryAddExperimentalNamedArguments(AttributeData attribute, List<string> arguments)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key is not ("Message" or "UrlFormat") ||
                argument.Value.Value is not (null or string))
            {
                return false;
            }

            arguments.Add(argument.Key + " = " + LiteralReader.ObjectLiteral(argument.Value.Value));
        }

        return true;
    }

    private static bool TryAddOptionalStringArgument(TypedConstant argument, List<string> arguments)
    {
        if (argument.Value is not (null or string))
        {
            return false;
        }

        arguments.Add(LiteralReader.ObjectLiteral(argument.Value));
        return true;
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

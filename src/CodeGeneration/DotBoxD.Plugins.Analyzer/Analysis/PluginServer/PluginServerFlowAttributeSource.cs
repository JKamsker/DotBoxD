using System.Text;
using DotBoxD.CodeGeneration.Shared.Defaults;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerFlowAttributeSource
{
    public static EquatableArray<string> MemberAttributes(IMethodSymbol method)
        => AttributeLines(method.GetAttributes(), targetReturn: false, includeExperimental: true);

    public static EquatableArray<string> ReturnAttributes(IMethodSymbol method)
        => AttributeLines(method.GetReturnTypeAttributes(), targetReturn: true, includeExperimental: true);

    public static EquatableArray<string> PropertyAttributes(IPropertySymbol property)
        => AttributeLines(property.GetAttributes(), targetReturn: false, includeExperimental: true);

    public static EquatableArray<string> TypeAttributes(INamedTypeSymbol type)
        => AttributeLines(type.GetAttributes(), targetReturn: false, includeExperimental: false);

    public static string ParameterAttributePrefix(IParameterSymbol parameter)
    {
        var builder = new StringBuilder();
        foreach (var attribute in parameter.GetAttributes())
        {
            if (CallerInfoAttributeFormatter.TryAppend(builder, attribute))
            {
                continue;
            }

            switch (GetFrameworkAttributeName(attribute))
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
        bool targetReturn,
        bool includeExperimental)
    {
        var lines = new List<string>();
        foreach (var attribute in attributes)
        {
            if (AttributeLine(attribute, targetReturn, includeExperimental) is { } line)
            {
                lines.Add(line);
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return new EquatableArray<string>(lines.ToArray());
    }

    private static string? AttributeLine(AttributeData attribute, bool targetReturn, bool includeExperimental)
    {
        switch (GetFrameworkAttributeName(attribute))
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
                if (!includeExperimental)
                {
                    return null;
                }

                return MemberOnlyAttribute(targetReturn, PluginServerExperimentalAttributeFormatter.Format(attribute));

            default:
                return null;
        }
    }

    private static string? GetFrameworkAttributeName(AttributeData attribute)
    {
        var attributeClass = attribute.AttributeClass;
        return attributeClass is not null &&
               attributeClass.Locations.Any(static location => location.IsInMetadata) &&
               HasFrameworkPublicKeyToken(attributeClass.ContainingAssembly.Identity)
            ? attributeClass.ToDisplayString()
            : null;
    }

    private static bool HasFrameworkPublicKeyToken(AssemblyIdentity identity)
    {
        var token = identity.PublicKeyToken;
        return TokenEquals(token, 0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e) ||
               TokenEquals(token, 0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89) ||
               TokenEquals(token, 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a);
    }

    private static bool TokenEquals(
        System.Collections.Immutable.ImmutableArray<byte> token,
        byte b0,
        byte b1,
        byte b2,
        byte b3,
        byte b4,
        byte b5,
        byte b6,
        byte b7) =>
        token.Length == 8 &&
        token[0] == b0 && token[1] == b1 && token[2] == b2 && token[3] == b3 &&
        token[4] == b4 && token[5] == b5 && token[6] == b6 && token[7] == b7;

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

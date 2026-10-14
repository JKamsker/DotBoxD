using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerPlatformCompatibilityAttributeFormatter
{
    public static string? Format(AttributeData attribute)
    {
        var attributeType = GetAttributeType(attribute);
        if (attributeType is null)
        {
            return null;
        }

        var arguments = FormatArguments(attribute);
        return arguments is null ? null : "[" + attributeType + "(" + string.Join(", ", arguments) + ")]";
    }

    private static string? GetAttributeType(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString() switch
        {
            "System.Runtime.Versioning.SupportedOSPlatformAttribute" =>
                "global::System.Runtime.Versioning.SupportedOSPlatformAttribute",
            "System.Runtime.Versioning.SupportedOSPlatformGuardAttribute" =>
                "global::System.Runtime.Versioning.SupportedOSPlatformGuardAttribute",
            "System.Runtime.Versioning.UnsupportedOSPlatformAttribute" =>
                "global::System.Runtime.Versioning.UnsupportedOSPlatformAttribute",
            "System.Runtime.Versioning.UnsupportedOSPlatformGuardAttribute" =>
                "global::System.Runtime.Versioning.UnsupportedOSPlatformGuardAttribute",
            "System.Runtime.Versioning.ObsoletedOSPlatformAttribute" =>
                "global::System.Runtime.Versioning.ObsoletedOSPlatformAttribute",
            _ => null,
        };

    private static List<string>? FormatArguments(AttributeData attribute)
    {
        var arguments = new List<string>();
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Value is not string)
            {
                return null;
            }

            arguments.Add(LiteralReader.StringLiteral((string)argument.Value));
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Value.Value is not string value)
            {
                return null;
            }

            arguments.Add(argument.Key + " = " + LiteralReader.StringLiteral(value));
        }

        return arguments;
    }
}

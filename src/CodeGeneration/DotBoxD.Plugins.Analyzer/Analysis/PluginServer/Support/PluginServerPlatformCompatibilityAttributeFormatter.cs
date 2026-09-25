using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerPlatformCompatibilityAttributeFormatter
{
    public static string? Format(AttributeData attribute)
    {
        var attributeType = attribute.AttributeClass?.ToDisplayString() switch
        {
            "System.Runtime.Versioning.SupportedOSPlatformAttribute" =>
                "global::System.Runtime.Versioning.SupportedOSPlatformAttribute",
            "System.Runtime.Versioning.UnsupportedOSPlatformAttribute" =>
                "global::System.Runtime.Versioning.UnsupportedOSPlatformAttribute",
            "System.Runtime.Versioning.ObsoletedOSPlatformAttribute" =>
                "global::System.Runtime.Versioning.ObsoletedOSPlatformAttribute",
            _ => null,
        };
        if (attributeType is null)
        {
            return null;
        }

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

        return "[" + attributeType + "(" + string.Join(", ", arguments) + ")]";
    }
}

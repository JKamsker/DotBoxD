using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerCodeRequirementAttributeFormatter
{
    public static string? Format(AttributeData attribute)
    {
        var attributeType = GetAttributeType(attribute);
        if (attributeType is null)
        {
            return null;
        }

        if (!TryFormatArguments(attribute, attributeType, out var arguments))
        {
            return null;
        }

        return arguments.Count == 0
            ? "[" + attributeType + "]"
            : "[" + attributeType + "(" + string.Join(", ", arguments) + ")]";
    }

    private static string? GetAttributeType(AttributeData attribute) =>
        attribute.AttributeClass?.ToDisplayString() switch
        {
            "System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute" =>
                "global::System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute",
            "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute" =>
                "global::System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute",
            "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute" =>
                "global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute",
            "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute" =>
                "global::System.Runtime.Versioning.RequiresPreviewFeaturesAttribute",
            _ => null,
        };

    private static bool TryFormatArguments(
        AttributeData attribute,
        string attributeType,
        out List<string> arguments)
    {
        arguments = new List<string>();
        if (!TryGetMessage(attribute, attributeType, out var message))
        {
            return false;
        }

        if (message is not null)
        {
            arguments.Add(LiteralReader.StringLiteral(message));
        }
        foreach (var argument in attribute.NamedArguments)
        {
            if (!TryFormatNamedArgument(argument, out var formatted))
            {
                return false;
            }

            arguments.Add(formatted);
        }

        return true;
    }

    private static bool TryGetMessage(AttributeData attribute, string attributeType, out string? message)
    {
        message = null;
        if (attribute.ConstructorArguments.Length == 0)
        {
            return attributeType == "global::System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute";
        }

        if (attribute.ConstructorArguments.Length != 1)
        {
            return false;
        }

        message = attribute.ConstructorArguments[0].Value as string;
        return message is not null;
    }

    private static bool TryFormatNamedArgument(
        KeyValuePair<string, TypedConstant> argument,
        out string formatted)
    {
        formatted = string.Empty;
        if (argument.Key != "Url")
        {
            return false;
        }

        if (argument.Value.Value is not (null or string))
        {
            return false;
        }

        formatted = "Url = " + LiteralReader.ObjectLiteral(argument.Value.Value);
        return true;
    }
}

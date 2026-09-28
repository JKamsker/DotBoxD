using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerCodeRequirementAttributeFormatter
{
    public static string? Format(AttributeData attribute)
    {
        var attributeType = attribute.AttributeClass?.ToDisplayString() switch
        {
            "System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute" =>
                "global::System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute",
            "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute" =>
                "global::System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute",
            "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute" =>
                "global::System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute",
            _ => null,
        };
        if (attributeType is null ||
            attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string message)
        {
            return null;
        }

        var arguments = new List<string> { LiteralReader.StringLiteral(message) };
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key != "Url" || argument.Value.Value is not (null or string))
            {
                return null;
            }

            arguments.Add("Url = " + LiteralReader.ObjectLiteral(argument.Value.Value));
        }

        return "[" + attributeType + "(" + string.Join(", ", arguments) + ")]";
    }
}

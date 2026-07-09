using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerExperimentalAttributeFormatter
{
    public static string? Format(AttributeData attribute)
    {
        if (!TryConstructorArguments(attribute, out var arguments) ||
            !TryAddNamedArguments(attribute, arguments))
        {
            return null;
        }

        return "[global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute(" + string.Join(", ", arguments) + ")]";
    }

    private static bool TryConstructorArguments(AttributeData attribute, out List<string> arguments)
    {
        arguments = [];
        if (attribute.ConstructorArguments is { Length: not (1 or 2) } ||
            attribute.ConstructorArguments[0].Value is not string diagnosticId)
        {
            return false;
        }

        arguments.Add(LiteralReader.StringLiteral(diagnosticId));
        return attribute.ConstructorArguments.Length == 1 ||
            TryAddOptionalStringArgument(attribute.ConstructorArguments[1], arguments);
    }

    private static bool TryAddNamedArguments(AttributeData attribute, List<string> arguments)
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
}
